"""M4 acceptance on the regtest dev server: settings screen, per-plan rules, crediting initial payments and renewals.
Never prints secrets (admin password, API key, wallet data)."""
import json, pathlib, sys, time, urllib.request
from playwright.sync_api import sync_playwright
sys.path.insert(0, str(pathlib.Path(__file__).parent))
BASE = "http://127.0.0.1:14142"
ROOT = pathlib.Path(__file__).resolve().parents[1]
SHOTS = ROOT / "docs/screens/m4"; SHOTS.mkdir(parents=True, exist_ok=True)
email, pw = (ROOT / "dev/.btcpay-data/dev-admin.txt").read_text().split()[:2]
AUTH = "token " + (ROOT / "dev/.btcpay-data/api-key.txt").read_text().strip()
import acceptance_m3_helpers as h  # rpc/psql helpers shared with M3 (no secrets printed)
results = []
def check(name, cond, extra=""):
    results.append((name, bool(cond))); print(("PASS " if cond else "FAIL ") + name + (f"  [{extra}]" if extra and not cond else ""))

def gf(method, path, body=None):
    req = urllib.request.Request(BASE + path, method=method, data=json.dumps(body).encode() if body is not None else None,
                                 headers={"Authorization": AUTH, "Content-Type": "application/json"})
    try:
        with urllib.request.urlopen(req) as r: return json.loads(r.read() or b"null")
    except urllib.error.HTTPError as e:
        return {"httpError": e.code, "body": e.read().decode()[:300]}

S = "\"BTCPayServer.Plugins.MaxiAffiliates\""
def commission(inv_id, wait=30):
    for _ in range(wait):
        row = h.psql(f"select c.\"CommissionAmount\", c.\"BaseCurrency\", c.\"AmountSats\", a.\"Code\", c.\"Status\", "
                     f"round(extract(epoch from c.\"PayableAt\" - c.\"CreatedAt\")/86400), c.\"BaseAmount\" "
                     f"from {S}.\"Commissions\" c join {S}.\"Affiliates\" a on a.\"Id\" = c.\"AffiliateId\" where c.\"InvoiceId\" = '{inv_id}'")
        if row: p = row.split("|"); return dict(amount=float(p[0]), cur=p[1], sats=int(p[2]), code=p[3], status=p[4], hold=int(float(p[5])), base=float(p[6]))
        if wait == 0: break
        time.sleep(1)
    return None

def pay(store, inv_id):
    pm = [m for m in gf("GET", f"/api/v1/stores/{store}/invoices/{inv_id}/payment-methods") if m["paymentMethodId"] == "BTC-CHAIN"][0]
    h.rpc("createwallet", "maxiaff") if "error" in (h.rpc("loadwallet", "maxiaff") or {}) else None
    addr = h.rpc("getnewaddress", wallet="maxiaff")
    if h.rpc("getbalance", wallet="maxiaff") < 1: h.rpc("generatetoaddress", 101, addr)
    h.rpc("sendtoaddress", pm["destination"], pm["due"], wallet="maxiaff"); h.rpc("generatetoaddress", 2, addr)
    for _ in range(60):
        st = gf("GET", f"/api/v1/stores/{store}/invoices/{inv_id}")["status"]
        if st == "Settled": return st
        time.sleep(2)
    return st

def checkout(store, offering, plan, *, ref=None, new_email=None, selector=None, credit=None):
    body = {"storeId": store, "offeringId": offering, "planId": plan, "invoiceMetadata": {"maxiaffRef": ref} if ref else {}}
    if new_email: body["newSubscriberEmail"] = new_email
    if selector: body["customerSelector"] = selector
    if credit: body["creditPurchase"] = credit
    co = gf("POST", "/api/v1/plan-checkout", body)
    if "id" not in co: return None, co
    co = gf("POST", f"/api/v1/plan-checkout/{co['id']}")
    return co.get("invoiceId"), co

store = gf("GET", "/api/v1/stores")[0]["id"]
ts = int(time.time())
off = gf("POST", f"/api/v1/stores/{store}/offerings", {"appName": f"Cointeligencia M4 {ts}"}); offering = off.get("id") or off.get("offeringId")
mk = lambda n: gf("POST", f"/api/v1/stores/{store}/offerings/{offering}/plans", {"name": n, "price": "10", "currency": "USD", "recurringType": "Monthly", "optimisticActivation": False})["id"]
pro, intro = mk("Pro Monthly"), mk("Intro Monthly")
check("offering + 2 plans (USD 10) created", offering and pro and intro)

with sync_playwright() as p:
    b = p.chromium.launch(channel="chrome", headless=True, args=["--no-sandbox", "--disable-dev-shm-usage"])
    pg = b.new_context(viewport={"width": 1280, "height": 1000}).new_page()
    pg.goto(f"{BASE}/login"); pg.fill("#Email", email); pg.fill("#Password", pw); pg.click("#LoginButton"); pg.wait_for_load_state("networkidle")
    pg.goto(f"{BASE}/stores/{store}/plugins/maxiaffiliates/affiliates")
    ANA = pg.locator('tr[data-affiliate="ana@example.com"] code').inner_text()
    pg.click("#maxiaff-tab-settings"); pg.wait_for_load_state("networkidle")
    check("settings screen reachable from Affiliates tabs", "/plugins/maxiaffiliates/settings" in pg.url)
    # Jefe defaults: 20 % lifetime, 30 days hold, LN + on-chain, manual approval; base currency = store default
    pg.fill("#Settings_DefaultPercent", "20"); pg.select_option("#Settings_DefaultScope", "Lifetime"); pg.fill("#Settings_HoldDays", "30")
    pg.fill("#Settings_BaseCurrencyOverride", ""); pg.fill("#Settings_MinPayoutSats", "10000")
    pg.click("#maxiaff-save-settings"); pg.wait_for_load_state("networkidle")
    check("settings saved via UI", pg.locator(".alert-success").count() and pg.locator("#Settings_HoldDays").input_value() == "30")
    check("effective base currency shown = store default (USD)", "Currently used: USD" in pg.locator("#maxiaff-effective-currency").inner_text())
    # invalid input rejected
    pg.fill("#Settings_BaseCurrencyOverride", "US$"); pg.click("#maxiaff-save-settings"); pg.wait_for_load_state("networkidle")
    check("invalid currency code rejected", pg.locator(".alert-danger").count() == 1 and pg.locator("#Settings_BaseCurrencyOverride").input_value() == "")
    # per-plan rule: Intro plan pays 50 % on the first payment only
    pg.select_option("#Rule_PlanId", intro); pg.select_option("#Rule_Type", "Percent"); pg.fill("#Rule_Value", "50"); pg.select_option("#Rule_Scope", "FirstPayment")
    pg.click("#maxiaff-save-rule"); pg.wait_for_load_state("networkidle")
    check("per-plan rule saved and listed", pg.locator("#maxiaff-rules tbody tr").filter(has_text="Intro Monthly").count() >= 1
          and h.psql(f"select round(\"Value\")::int || '/' || \"Scope\" from {S}.\"CommissionRules\" where \"PlanId\" = '{intro}'") == "50/FirstPayment")
    pg.screenshot(path=str(SHOTS / "01-settings-and-rules.png"), full_page=True)

    # 1) Pro plan, default rule 20 % lifetime: initial + renewal
    buyer = f"m4buyer{ts}@example.com"
    inv1, co = checkout(store, offering, pro, ref=ANA, new_email=buyer)
    check("initial Pro checkout invoice created with ref", inv1, str(co)[:200])
    check("initial invoice settles on regtest", pay(store, inv1) == "Settled")
    c1 = commission(inv1)
    check("initial payment credited: 20% of USD 10 = 2 USD, Pending, 30-day hold, to ANA",
          c1 and abs(c1["amount"] - 2) < 1e-9 and c1["cur"] == "USD" and c1["code"] == ANA and c1["status"] == "Pending" and c1["hold"] == 30 and c1["sats"] > 0, str(c1))
    inv2, co = checkout(store, offering, pro, selector=f"Email:{buyer}", credit=10)
    check("renewal invoice for existing subscriber (no ref on it)", inv2, str(co)[:300])
    meta2 = gf("GET", f"/api/v1/stores/{store}/invoices/{inv2}").get("metadata", {}) if inv2 else {}
    check("renewal invoice carries no maxiaffRef", inv2 and "maxiaffRef" not in meta2)
    check("renewal settles", inv2 and pay(store, inv2) == "Settled")
    c2 = commission(inv2) if inv2 else None
    check("renewal credited to the same affiliate (lifetime)", c2 and c2["code"] == ANA and abs(c2["amount"] - 2) < 1e-9, str(c2))

    # 2) Intro plan, plan rule 50 % first payment only
    buyer2 = f"m4intro{ts}@example.com"
    inv3, _ = checkout(store, offering, intro, ref=ANA, new_email=buyer2); pay(store, inv3)
    c3 = commission(inv3)
    check("Intro first payment: plan rule 50% = 5 USD", c3 and abs(c3["amount"] - 5) < 1e-9, str(c3))
    inv4, co = checkout(store, offering, intro, selector=f"Email:{buyer2}", credit=10); pay(store, inv4)
    time.sleep(5)
    check("Intro renewal: out of scope, no commission", inv4 and commission(inv4, wait=0) is None)

    # 3) Base currency override: EUR (the dev store rate script (kraken) has no MXN pair)
    pg.goto(f"{BASE}/stores/{store}/plugins/maxiaffiliates/settings")
    pg.fill("#Settings_BaseCurrencyOverride", "eur"); pg.click("#maxiaff-save-settings"); pg.wait_for_load_state("networkidle")
    check("override saved and shown as effective currency", "Currently used: EUR" in pg.locator("#maxiaff-effective-currency").inner_text())
    inv5, _ = checkout(store, offering, pro, selector=f"Email:{buyer}", credit=10); pay(store, inv5)
    c5 = commission(inv5)
    check("renewal with EUR override: commission in EUR = 20% of converted sale", c5 and c5["cur"] == "EUR" and 5 < c5["base"] < 10 and abs(c5["amount"] - c5["base"] * 0.2) < 0.01, str(c5))
    pg.fill("#Settings_BaseCurrencyOverride", ""); pg.click("#maxiaff-save-settings"); pg.wait_for_load_state("networkidle")

    cust = h.psql(f"select \"CreditedPayments\" from {S}.\"Referrals\" where \"CustomerKey\" = 'email:{buyer}'")
    check("referral binding counts 3 credited payments", cust == "3", cust)
    pg.goto(f"{BASE}/stores/{store}/plugins/maxiaffiliates/commissions"); pg.wait_for_load_state("networkidle")
    check("commissions list shows the credited invoices", all(pg.locator("#maxiaff-commissions").filter(has_text=i).count() for i in [inv1, inv2, inv3, inv5]))
    pg.screenshot(path=str(SHOTS / "02-commissions-list.png"), full_page=True)
    b.close()
fails = [n for n, ok in results if not ok]
print(f"{len(results)-len(fails)}/{len(results)} checks passed"); sys.exit(1 if fails else 0)
