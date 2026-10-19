"""M6 acceptance on regtest: payout cycle, commissions-due report + CSV, manual approval (default) and auto approval,
payouts through BTCPay store payouts + automated processors: Lightning (merchant CLN -> customer CLN channel, LNURL)
and on-chain (hot wallet). Run dev/ln_setup.py and dev/m6_setup.py first. Never prints secrets."""
import csv, io, json, pathlib, subprocess, sys, time
sys.path.insert(0, str(pathlib.Path(__file__).parent))
from playwright.sync_api import sync_playwright
import acceptance_m3_helpers as h
from acceptance_m4 import BASE, ROOT, email, pw, gf, checkout, pay, commission, S
import m6_setup

SHOTS = ROOT / "docs/screens/m6"; SHOTS.mkdir(parents=True, exist_ok=True)
results = []
def check(name, cond, extra=""):
    results.append((name, bool(cond))); print(("PASS " if cond else "FAIL ") + name + (f"  [{extra}]" if extra and not cond else ""))
def cln(node, *a):
    o = subprocess.run(["sudo", "docker", "exec", f"maxiaff-regtest-{node}_lightningd-1", "lightning-cli", "--network=regtest", *a], capture_output=True, text=True).stdout
    return json.loads(o or "{}")
def q(sql): return h.psql(sql)
def payout_state(store, pid):
    return {x["id"]: x["state"] for x in gf("GET", f"/api/v1/stores/{store}/payouts?includeCancelled=true")}.get(pid)
def statuses(invs):
    ids = ",".join("'" + i + "'" for i in invs)
    return q(f'select string_agg(distinct "Status", \',\') from {S}."Commissions" where "InvoiceId" in ({ids})')

env = m6_setup.setup(); store = env["store"]
check("regtest setup: store LN (merchant CLN), hot wallet, automated payout processors, affiliate LNURL",
      env["merchant_ln"] is True and env["customer_ln"] is True and env["oc_processor"].get("processNewPayoutsInstantly") and env["ln_processor"].get("processNewPayoutsInstantly"))
ts = int(time.time())
off = gf("POST", f"/api/v1/stores/{store}/offerings", {"appName": f"Cointeligencia M6 {ts}"}); offering = off.get("id") or off.get("offeringId")
plan = gf("POST", f"/api/v1/stores/{store}/offerings/{offering}/plans", {"name": "Pro Monthly", "price": "10", "currency": "USD", "recurringType": "Monthly", "optimisticActivation": False})["id"]
mine = h.rpc("getnewaddress", wallet="maxiaff")
OC_ADDR = h.rpc("getnewaddress", "", "bech32", wallet="maxiaff")
LN_AFF, OC_AFF = f"m6ln{ts}@example.com", f"m6oc{ts}@example.com"

with sync_playwright() as p:
    b = p.chromium.launch(channel="chrome", headless=True, args=["--no-sandbox", "--disable-dev-shm-usage"])
    ctx = b.new_context(viewport={"width": 1280, "height": 1000}); pg = ctx.new_page()
    pg.goto(f"{BASE}/login"); pg.fill("#Email", email); pg.fill("#Password", pw); pg.click("#LoginButton"); pg.wait_for_load_state("networkidle")
    def settings(**kw):
        pg.goto(f"{BASE}/stores/{store}/plugins/maxiaffiliates/settings")
        for k, v in kw.items():
            loc = pg.locator(f"#Settings_{k}")
            if isinstance(v, bool): loc.set_checked(v)
            elif loc.evaluate("e => e.tagName") == "SELECT": loc.select_option(str(v))
            else: loc.fill(str(v))
        pg.click("#maxiaff-save-settings"); pg.wait_for_load_state("networkidle")
        return pg.locator(".alert-success").count() > 0
    # affiliates: one Lightning (LNURL served by the customer node's store), one on-chain (regtest address)
    pg.goto(f"{BASE}/stores/{store}/plugins/maxiaffiliates/affiliates"); signup = pg.locator("#maxiaff-signup-url").inner_text()
    for mail, method, dest in [(LN_AFF, "Lightning", env["lnurl"]), (OC_AFF, "OnChain", OC_ADDR)]:
        a = b.new_context().new_page(); a.goto(signup)
        a.fill("#Email", mail); a.check(f'input[name="PayoutMethod"][value="{method}"]') if a.locator(f'input[name="PayoutMethod"][value="{method}"]').count() else a.select_option("#PayoutMethod", method)
        a.fill("#PayoutDestination", dest); a.click("#maxiaff-submit")
        pg.reload(); pg.locator(f'tr[data-affiliate="{mail}"] button[data-action="Approve"]').click(); pg.wait_for_load_state("networkidle")
    LN_CODE = pg.locator(f'tr[data-affiliate="{LN_AFF}"] code').inner_text(); OC_CODE = pg.locator(f'tr[data-affiliate="{OC_AFF}"] code').inner_text()
    check("LN affiliate (LNURL) and on-chain affiliate approved", LN_CODE and OC_CODE)
    check("settings: monthly cycle day 1, minimum 5,000 sats, hold 0, manual approval (default)",
          settings(HoldDays=0, MinPayoutSats=5000, PayoutFrequency="Monthly", PayoutDay=1, AutoApprovePayouts=False, LightningEnabled=True, OnChainEnabled=True))

    def sales(code, n):
        invs = []
        for i in range(n):
            inv, _ = checkout(store, offering, plan, ref=code, new_email=f"m6{code.lower()}{i}{ts}@example.com"); pay(store, inv); invs.append(inv)
        for inv in invs: commission(inv)
        return invs
    ln_invs = sales(LN_CODE, 3); oc_invs = sales(OC_CODE, 3)
    pg.goto(f"{BASE}/stores/{store}/plugins/maxiaffiliates/payouts")
    check("payouts page shows the schedule (monthly, next run, manual approval)", "Next scheduled run" in pg.locator("#maxiaff-cycle-info").inner_text() and "manual" in pg.locator("#maxiaff-cycle-info").inner_text())
    pg.click("#maxiaff-run-cycle"); pg.wait_for_load_state("networkidle")
    cycle_url = pg.url
    row = lambda m: pg.locator(f'#maxiaff-batches tr[data-affiliate="{m}"]')
    ln_sats = sum(commission(i)["sats"] for i in ln_invs); oc_sats = sum(commission(i)["sats"] for i in oc_invs)
    check("cycle report lists both affiliates as 'Awaiting approval' with their totals",
          row(LN_AFF).get_attribute("data-state") == "Draft" and row(OC_AFF).get_attribute("data-state") == "Draft"
          and row(LN_AFF).locator("td").nth(6).inner_text().replace(",", "") == str(ln_sats)
          and row(OC_AFF).locator("td").nth(6).inner_text().replace(",", "") == str(oc_sats))
    pg.screenshot(path=str(SHOTS / "02-commissions-due-report.png"), full_page=True)
    csv_resp = ctx.request.get(BASE + pg.locator("#maxiaff-csv").get_attribute("href"))
    rows = list(csv.DictReader(io.StringIO(csv_resp.text().lstrip("\ufeff"))))
    mine_rows = {r["affiliate_email"]: r for r in rows}
    check("CSV export: header + a row per payout with method, destination, sats, state",
          csv_resp.status == 200 and "text/csv" in csv_resp.headers.get("content-type", "")
          and mine_rows.get(LN_AFF, {}).get("payout_method") == "Lightning" and mine_rows[LN_AFF]["amount_sats"] == str(ln_sats)
          and mine_rows.get(OC_AFF, {}).get("destination") == OC_ADDR and mine_rows[OC_AFF]["state"] == "Draft", str(rows)[:200])
    # other (older dev) affiliates in the same cycle are cancelled so their balances carry over
    while True:
        others = [m for m in pg.locator('#maxiaff-batches tr[data-state="Draft"]').evaluate_all("rs => rs.map(r => r.dataset.affiliate)") if m not in (LN_AFF, OC_AFF)]
        if not others: break
        row(others[0]).locator('button:has-text("Cancel")').click(); pg.wait_for_load_state("networkidle")
    lnpaid_before = len([i for i in cln("customer", "listinvoices").get("invoices", []) if i["status"] == "paid"])
    for m in (LN_AFF, OC_AFF):
        row(m).locator('button:has-text("Approve")').click(); pg.wait_for_load_state("networkidle")
    ln_payout, oc_payout = row(LN_AFF).get_attribute("data-payout"), row(OC_AFF).get_attribute("data-payout")
    check("approval creates one BTCPay payout per affiliate", ln_payout and oc_payout, f"{row(LN_AFF).inner_text()[:200]} | {row(OC_AFF).inner_text()[:200]}")
    po = {x["id"]: x for x in gf("GET", f"/api/v1/stores/{store}/payouts?includeCancelled=false")}
    check("payouts carry the right method and amount", po.get(ln_payout, {}).get("payoutMethodId") == "BTC-LN" and po.get(oc_payout, {}).get("payoutMethodId") == "BTC-CHAIN"
          and abs(float(po[oc_payout].get("amount") or po[oc_payout]["originalAmount"]) * 1e8 - oc_sats) < 1, str(po.get(oc_payout))[:200])
    # processors pay instantly; confirm on-chain
    for _ in range(40):
        h.rpc("generatetoaddress", 1, mine); time.sleep(2)
        st = {k: payout_state(store, k) for k in (ln_payout, oc_payout)}
        if all(v == "Completed" for v in st.values()): break
    check("Lightning payout Completed (paid over the regtest channel)", st[ln_payout] == "Completed", str(st))
    check("on-chain payout Completed (hot wallet tx confirmed)", st[oc_payout] == "Completed", str(st))
    lnpaid_after = [i for i in cln("customer", "listinvoices").get("invoices", []) if i["status"] == "paid"]
    check("affiliate's node received the Lightning payment", len(lnpaid_after) == lnpaid_before + 1 and lnpaid_after[-1]["amount_received_msat"] >= ln_sats * 1000 - 1000, str(lnpaid_after[-1:])[:200])
    got = h.rpc("getreceivedbyaddress", OC_ADDR, 1, wallet="maxiaff")
    check("affiliate's on-chain address received the payout", abs(got * 1e8 - oc_sats) < 1, str(got))
    for _ in range(20):
        pg.goto(f"{BASE}/stores/{store}/plugins/maxiaffiliates/commissions"); pg.click("#maxiaff-run-maintenance"); pg.wait_for_load_state("networkidle")
        pg.goto(cycle_url)
        if row(LN_AFF).get_attribute("data-state") == "Completed" and row(OC_AFF).get_attribute("data-state") == "Completed": break
        time.sleep(2)
    check("batches marked Completed in the report", row(LN_AFF).get_attribute("data-state") == "Completed" and row(OC_AFF).get_attribute("data-state") == "Completed")
    st = statuses(ln_invs + oc_invs)
    check("commissions marked Paid", st == "Paid", st)
    pg.screenshot(path=str(SHOTS / "03-cycle-paid.png"), full_page=True)

    # auto approval: next cycle creates and pays the payout without a click
    check("enable automatic payout approval", settings(AutoApprovePayouts=True))
    oc2 = sales(OC_CODE, 3); oc2_sats = sum(commission(i)["sats"] for i in oc2)
    pg.goto(f"{BASE}/stores/{store}/plugins/maxiaffiliates/payouts"); pg.click("#maxiaff-run-cycle"); pg.wait_for_load_state("networkidle")
    r2 = row(OC_AFF)
    check("auto-approved cycle: payout created immediately", r2.get_attribute("data-state") in ("Approved", "Sent", "Completed") and r2.get_attribute("data-payout"), r2.inner_text()[:200])
    for _ in range(30):
        h.rpc("generatetoaddress", 1, mine); time.sleep(2)
        if payout_state(store, r2.get_attribute("data-payout")) == "Completed": break
    pg.goto(f"{BASE}/stores/{store}/plugins/maxiaffiliates/commissions"); pg.click("#maxiaff-run-maintenance"); pg.wait_for_load_state("networkidle")
    st = statuses(oc2)
    check("auto-approved payout paid on-chain and commissions Paid", st == "Paid" and abs(h.rpc("getreceivedbyaddress", OC_ADDR, 1, wallet="maxiaff") * 1e8 - oc_sats - oc2_sats) < 1, st)
    settings(AutoApprovePayouts=False)
    pg.goto(f"{BASE}/stores/{store}/plugins/maxiaffiliates/payouts")
    pg.screenshot(path=str(SHOTS / "01-payouts-cycles.png"), full_page=True)
    b.close()
fails = [n for n, ok in results if not ok]
print(f"{len(results)-len(fails)}/{len(results)} checks passed"); sys.exit(1 if fails else 0)
