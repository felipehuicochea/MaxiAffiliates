"""M5 acceptance on the regtest dev server: hold period, refund reversal / clawback, minimum payout threshold with carry-over,
rate-missing retry, and the M4 display polish. Never prints secrets."""
import json, pathlib, sys, time
sys.path.insert(0, str(pathlib.Path(__file__).parent))
from playwright.sync_api import sync_playwright
import acceptance_m3_helpers as h
from acceptance_m4 import BASE, ROOT, email, pw, gf, checkout, pay, commission, S  # reuse M4 helpers (module guarded below)

SHOTS = ROOT / "docs/screens/m5"; SHOTS.mkdir(parents=True, exist_ok=True)
results = []
def check(name, cond, extra=""):
    results.append((name, bool(cond))); print(("PASS " if cond else "FAIL ") + name + (f"  [{extra}]" if extra and not cond else ""))

store = gf("GET", "/api/v1/stores")[0]["id"]
ts = int(time.time())
off = gf("POST", f"/api/v1/stores/{store}/offerings", {"appName": f"Cointeligencia M5 {ts}"}); offering = off.get("id") or off.get("offeringId")
plan = gf("POST", f"/api/v1/stores/{store}/offerings/{offering}/plans", {"name": "Pro Monthly", "price": "10", "currency": "USD", "recurringType": "Monthly", "optimisticActivation": False})["id"]
AFF = f"m5aff{ts}@example.com"

def status_of(inv): return h.psql(f"select \"Status\" from {S}.\"Commissions\" where \"InvoiceId\" = '{inv}'")
def adj_of(inv): return int(h.psql(f"select coalesce(sum(a.\"AmountSats\"),0) from {S}.\"CommissionAdjustments\" a join {S}.\"Commissions\" c on c.\"Id\" = a.\"CommissionId\" where c.\"InvoiceId\" = '{inv}'") or 0)
def wait(fn, pred, n=20):
    v = None
    for _ in range(n):
        v = fn()
        if pred(v): return v
        time.sleep(1)
    return v

with sync_playwright() as p:
    b = p.chromium.launch(channel="chrome", headless=True, args=["--no-sandbox", "--disable-dev-shm-usage"])
    pg = b.new_context(viewport={"width": 1280, "height": 1000}).new_page()
    pg.goto(f"{BASE}/login"); pg.fill("#Email", email); pg.fill("#Password", pw); pg.click("#LoginButton"); pg.wait_for_load_state("networkidle")
    def settings(**kw):
        pg.goto(f"{BASE}/stores/{store}/plugins/maxiaffiliates/settings")
        for k, v in kw.items(): pg.fill(f"#Settings_{k}", str(v))
        pg.click("#maxiaff-save-settings"); pg.wait_for_load_state("networkidle")
        return pg.locator(".alert-success").count() > 0
    def process():
        pg.goto(f"{BASE}/stores/{store}/plugins/maxiaffiliates/commissions"); pg.click("#maxiaff-run-maintenance"); pg.wait_for_load_state("networkidle")
    def balance():
        pg.goto(f"{BASE}/stores/{store}/plugins/maxiaffiliates/commissions")
        row = pg.locator(f'#maxiaff-balances tr[data-affiliate="{AFF}"]')
        return (row.get_attribute("data-state"), int(row.get_attribute("data-net"))) if row.count() else (None, 0)

    # fresh affiliate for this run
    pg.goto(f"{BASE}/stores/{store}/plugins/maxiaffiliates/affiliates")
    anon = b.new_context().new_page(); anon.goto(pg.locator("#maxiaff-signup-url").inner_text())
    anon.fill("#Email", AFF); anon.fill("#PayoutDestination", f"m5{ts}@ln.example"); anon.click("#maxiaff-submit")
    pg.reload(); pg.locator(f'tr[data-affiliate="{AFF}"] button[data-action="Approve"]').click(); pg.wait_for_load_state("networkidle")
    CODE = pg.locator(f'tr[data-affiliate="{AFF}"] code').inner_text()
    check("settings: hold 0 days, min payout 5,000 sats, base = store default", settings(HoldDays=0, MinPayoutSats=5000, BaseCurrencyOverride=""))

    def buy(n):
        inv, _ = checkout(store, offering, plan, ref=CODE, new_email=f"m5b{n}{ts}@example.com"); pay(store, inv); return inv
    # 1) hold period + threshold carry-over
    i1 = buy(1); c1 = commission(i1)
    check("sale credited as Pending (hold)", c1 and c1["status"] == "Pending", str(c1))
    process()
    check("after the hold, 'process now' makes it Payable", status_of(i1) == "Payable")
    st, net = balance()
    check("one commission (~2.4k sats) is below the 5,000 minimum: carries over", st == "BelowThreshold" and net == c1["sats"], f"{st} {net}")
    i2 = buy(2); i3 = buy(3); commission(i3); process()
    st, net = balance()
    check("three commissions reach the minimum: Due", st == "Due" and net == c1["sats"] + commission(i2)["sats"] + commission(i3)["sats"], f"{st} {net}")
    settings(HoldDays=30)
    i4 = buy(4); commission(i4); process()
    check("with a 30-day hold, a new sale stays Pending after processing", status_of(i4) == "Pending")
    pg.goto(f"{BASE}/stores/{store}/plugins/maxiaffiliates/commissions")
    on_hold = pg.locator(f'#maxiaff-balances tr[data-affiliate="{AFF}"] td').nth(1).inner_text().replace(",", "")
    check("balances show the on-hold amount separately", int(on_hold) == commission(i4)["sats"], on_hold)
    settings(HoldDays=0)

    # 2) refunds
    r = gf("POST", f"/api/v1/stores/{store}/invoices/{i4}/refund", {"refundVariant": "Fiat", "payoutMethods": ["BTC-CHAIN"]})
    check("full refund (Greenfield) of the on-hold sale -> commission Reversed", "id" in r and wait(lambda: status_of(i4), lambda v: v == "Reversed") == "Reversed", str(r)[:200])
    r = gf("POST", f"/api/v1/stores/{store}/invoices/{i2}/refund", {"refundVariant": "Custom", "customAmount": "2.5", "customCurrency": "USD", "payoutMethods": ["BTC-CHAIN"]})
    claw = wait(lambda: adj_of(i2), lambda v: v != 0)
    check("partial refund of 25% of a payable sale -> clawback of 25% of its sats", "id" in r and claw == -int(commission(i2)["sats"] * 0.25 + 0.5) and status_of(i2) == "Payable", f"{claw} {str(r)[:150]}")
    # paid commission (payouts arrive in M6, so mark Paid directly in the dev DB) then refund -> netted against future balance
    h.psql(f"update {S}.\"Commissions\" set \"Status\" = 'Paid', \"PaidAt\" = now() where \"InvoiceId\" in ('{i1}','{i3}')")
    gf("POST", f"/api/v1/stores/{store}/invoices/{i1}/refund", {"refundVariant": "Fiat", "payoutMethods": ["BTC-CHAIN"]})
    claw1 = wait(lambda: adj_of(i1), lambda v: v != 0)
    check("refund after payout -> full clawback entry, status stays Paid", claw1 == -commission(i1)["sats"] and status_of(i1) == "Paid", str(claw1))
    st, net = balance()
    expected = commission(i2)["sats"] + claw + claw1
    check("net balance = payable - clawbacks, below minimum or negative carries over", net == expected and st in ("BelowThreshold", "NegativeCarryOver"), f"{st} {net} != {expected}")
    i5 = buy(5); commission(i5)
    r = gf("POST", f"/api/v1/stores/{store}/invoices/{i5}/status", {"status": "Invalid"})
    # BTCPay 2.4.5 refuses Settled -> Invalid through the API; the plugin's invalid handler is covered by integration tests.
    check("BTCPay refuses marking a settled invoice invalid; commission untouched", r.get("httpError") == 422 and status_of(i5) in ("Pending", "Payable"), str(r)[:200])
    gf("POST", f"/api/v1/stores/{store}/invoices/{i4}/refund", {"refundVariant": "Fiat", "payoutMethods": ["BTC-CHAIN"]})
    time.sleep(2)
    check("repeated refund is idempotent", status_of(i4) == "Reversed" and adj_of(i4) == 0)

    # 3) missing rate -> waiting list -> retry after the store gets a rate source
    rates0 = gf("GET", f"/api/v1/stores/{store}/rates/configuration/primary")
    settings(BaseCurrencyOverride="MXN")
    i6 = buy(6)
    pending = wait(lambda: h.psql(f"select \"Attempts\" from {S}.\"PendingCredits\" where \"InvoiceId\" = '{i6}'"), lambda v: v != "")
    pg.goto(f"{BASE}/stores/{store}/plugins/maxiaffiliates/commissions")
    check("no MXN rate: payment listed as waiting for a rate, not credited", pending == "1" and commission(i6, wait=0) is None and pg.locator("#maxiaff-pending-credits").filter(has_text=i6).count() == 1, pending)
    pg.screenshot(path=str(SHOTS / "01-waiting-for-rate.png"), full_page=True)
    put = gf("PUT", f"/api/v1/stores/{store}/rates/configuration/primary",
             {"isCustomScript": True, "spread": "0", "effectiveScript": rates0["effectiveScript"] + "\nX_MXN = yadio(X_MXN);"})
    process()
    c6 = commission(i6, wait=5)
    check("after adding an MXN rate source, retry credits it in MXN", c6 and c6["cur"] == "MXN" and c6["base"] > 50 and h.psql(f"select count(*) from {S}.\"PendingCredits\" where \"InvoiceId\" = '{i6}'") == "0", f"{c6} {str(put)[:200]}")
    gf("PUT", f"/api/v1/stores/{store}/rates/configuration/primary", {"isCustomScript": False, "spread": "0", "preferredSource": rates0.get("preferredSource") or "kraken"})
    restored = gf("GET", f"/api/v1/stores/{store}/rates/configuration/primary")
    check("store rate configuration restored", restored.get("isCustomScript") is False, str(restored)[:120])

    # 4) polish: fiat rounded to currency decimals, friendly scope labels
    pg.goto(f"{BASE}/stores/{store}/plugins/maxiaffiliates/commissions")
    txt = pg.locator(f'#maxiaff-commissions tr[data-invoice="{i6}"] td').nth(5).inner_text()
    check("fiat commission shown with 2 decimals", txt.endswith(" MXN") and len(txt.split()[0].split(".")[-1]) == 2, txt)
    pg.screenshot(path=str(SHOTS / "02-balances-and-ledger.png"), full_page=False)
    settings(BaseCurrencyOverride="")
    pg.goto(f"{BASE}/stores/{store}/plugins/maxiaffiliates/settings")
    opts = pg.locator("#Settings_DefaultScope option").all_inner_texts()
    check("friendly scope labels", opts == ["First payment only", "Every payment (lifetime)", "First N payments"], str(opts))
    pg.screenshot(path=str(SHOTS / "03-settings-friendly-labels.png"), full_page=True)
    b.close()
fails = [n for n, ok in results if not ok]
print(f"{len(results)-len(fails)}/{len(results)} checks passed"); sys.exit(1 if fails else 0)
