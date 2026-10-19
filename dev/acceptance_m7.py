"""M7 acceptance on regtest: affiliate panel (admin-issued token link when the store has no email, magic link by email
via a local Mailpit SMTP sink when it does), stats/balance/commissions/payouts, payout address update, and the admin
reports in BTCPay's Reporting page. Start the stack with `--profile mail`. Never prints secrets (tokens are masked in screenshots)."""
import json, pathlib, re, sys, time, urllib.parse, urllib.request
sys.path.insert(0, str(pathlib.Path(__file__).parent))
from playwright.sync_api import sync_playwright
import acceptance_m3_helpers as h
from acceptance_m4 import BASE, ROOT, email, pw, gf, checkout, pay, commission, S

MAIL = "http://127.0.0.1:39026"
SHOTS = ROOT / "docs/screens/m7"; SHOTS.mkdir(parents=True, exist_ok=True)
results = []
def check(name, cond, extra=""):
    results.append((name, bool(cond))); print(("PASS " if cond else "FAIL ") + name + (f"  [{extra}]" if extra and not cond else ""))
def mail(path, method="GET"):
    with urllib.request.urlopen(urllib.request.Request(MAIL + path, method=method)) as r:
        t = r.read(); return json.loads(t) if method == "GET" else None
def inbox(to):
    return [m for m in mail("/api/v1/messages")["messages"] if any(t["Address"] == to for t in m["To"])]
def mask(page):
    page.evaluate("""() => { for (const e of document.querySelectorAll('input')) if (/token=/.test(e.value)) e.value = e.value.replace(/token=[^&]+/, 'token=••••••••'); }""")
def shot(page, name, full=True): page.screenshot(path=str(SHOTS / name), full_page=full)

store = gf("GET", "/api/v1/stores")[0]["id"]
ts = int(time.time())
off = gf("POST", f"/api/v1/stores/{store}/offerings", {"appName": f"Cointeligencia M7 {ts}"}); offering = off.get("id") or off.get("offeringId")
plan = gf("POST", f"/api/v1/stores/{store}/offerings/{offering}/plans", {"name": "Pro Monthly", "price": "10", "currency": "USD", "recurringType": "Monthly", "optimisticActivation": False})["id"]
AFF = f"m7aff{ts}@example.com"
h.rpc("loadwallet", "maxiaff")
ADDR1 = h.rpc("getnewaddress", "", "bech32", wallet="maxiaff"); ADDR2 = h.rpc("getnewaddress", "", "bech32", wallet="maxiaff")
NO_EMAIL = {"server": "", "port": None, "login": "", "password": "", "from": "", "disableCertificateCheck": False}
gf("PUT", f"/api/v1/stores/{store}/email", NO_EMAIL)

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
    def maintenance():
        pg.goto(f"{BASE}/stores/{store}/plugins/maxiaffiliates/commissions"); pg.click("#maxiaff-run-maintenance"); pg.wait_for_load_state("networkidle")
    settings(HoldDays=0, MinPayoutSats=5000, PayoutFrequency="Monthly", PayoutDay=1, AutoApprovePayouts=False, LightningEnabled=True, OnChainEnabled=True)

    # signup (on-chain), approve
    adm = f"{BASE}/stores/{store}/plugins/maxiaffiliates/affiliates"
    pg.goto(adm); signup = pg.locator("#maxiaff-signup-url").inner_text(); panel_login = pg.locator("#maxiaff-panel-url").inner_text()
    a = b.new_context(viewport={"width": 1280, "height": 1000}); ap = a.new_page(); ap.goto(signup)
    ap.fill("#Email", AFF); ap.check('input[name="PayoutMethod"][value="OnChain"]'); ap.fill("#PayoutDestination", ADDR1); ap.click("#maxiaff-submit")
    pg.reload(); pg.locator(f'tr[data-affiliate="{AFF}"] button[data-action="Approve"]').click(); pg.wait_for_load_state("networkidle")
    CODE = pg.locator(f'tr[data-affiliate="{AFF}"] code').inner_text()
    if not pg.locator("#landingUrl").input_value():
        pg.fill("#landingUrl", "http://127.0.0.1:14150/"); pg.click("#maxiaff-save-landing"); pg.wait_for_load_state("networkidle")

    # 1) no store email -> login page says so; admin issues a secure token link
    ap.goto(panel_login)
    check("no email configured: panel login explains to ask the store for a link", ap.locator("#maxiaff-panel-noemail").count() == 1 and pg.locator("#maxiaff-noemail-badge").count() == 1)
    shot(ap, "01-panel-login-no-email.png")
    pg.locator(f'tr[data-affiliate="{AFF}"] button[data-action="PanelLink"]').click(); pg.wait_for_load_state("networkidle")
    link = pg.locator("#maxiaff-admin-panel-link").input_value()
    check("admin 'Panel link' shows a one-time-displayed 256-bit token link", re.search(r"/panel/auth\?token=[A-Za-z0-9_-]{43}$", link) is not None)
    mask(pg); shot(pg, "02-admin-affiliates-panel-link.png", full=False)
    pg.reload(); check("link is shown once (gone after reload)", pg.locator("#maxiaff-admin-panel-link").count() == 0)
    check("token is stored only as a SHA-256 hash", h.psql(f"select count(*) from {S}.\"AccessTokens\" where \"TokenHash\" = '{link.split('=')[-1]}'") == "0")

    ap.goto(link)
    check("opening the link does not sign in by itself (scanner-safe confirm)", ap.locator("#maxiaff-panel-continue").count() == 1 and not [c for c in a.cookies() if c["name"] == "maxiaff_panel"])
    shot(ap, "03-panel-link-confirm.png")
    ap.click("#maxiaff-panel-continue"); ap.wait_for_load_state("networkidle")
    ck = [c for c in a.cookies() if c["name"] == "maxiaff_panel"]
    check("signed in: HttpOnly, SameSite=Lax session cookie scoped to the panel path",
          ap.locator("#maxiaff-panel").count() == 1 and ck and ck[0]["httpOnly"] and ck[0]["sameSite"] == "Lax" and ck[0]["path"] == f"/plugins/maxiaffiliates/{store}/panel")
    check("empty panel: no commissions / payouts yet", "No commissions yet" in ap.inner_text("body") and "No payouts yet" in ap.inner_text("body"))
    # a second browser reuses the same admin link (it's a bookmarkable 30-day link until reissued)
    other = b.new_context(); op = other.new_page(); op.goto(link); op.click("#maxiaff-panel-continue"); op.wait_for_load_state("networkidle")
    check("admin link works on a second device", op.locator("#maxiaff-panel").count() == 1)
    anon = b.new_context().new_page(); anon.goto(f"{BASE}/plugins/maxiaffiliates/{store}/panel")
    check("panel without a session redirects to login", "/panel/login" in anon.url)

    # 2) activity: 2 clicks, 3 paid sales -> payable -> cycle -> approved payout; then 2 sales on a 14-day hold
    clicks = [b.new_context().request.get(f"{BASE}/aff/{store}/{CODE}", max_redirects=0).status for _ in range(2)]
    invs = []
    for i in range(3):
        inv, _ = checkout(store, offering, plan, ref=CODE, new_email=f"m7buyer{i}{ts}@example.com"); pay(store, inv); invs.append(inv)
    for inv in invs: commission(inv)
    maintenance()
    pg.goto(f"{BASE}/stores/{store}/plugins/maxiaffiliates/payouts"); pg.click("#maxiaff-run-cycle"); pg.wait_for_load_state("networkidle")
    row = pg.locator(f'#maxiaff-batches tr[data-affiliate="{AFF}"]')
    if row.count():
        row.locator('button[value="approve"]').click(); pg.wait_for_load_state("networkidle")
    mine = h.rpc("getnewaddress", wallet="maxiaff")
    st = None
    for _ in range(20):
        st = h.psql(f'select "State" from {S}."PayoutBatches" b join {S}."Affiliates" a on a."Id" = b."AffiliateId" where a."Email" = \'{AFF}\' and b."State" <> \'Cancelled\'')
        if st == "Completed": break
        h.rpc("generatetoaddress", 1, mine); time.sleep(2); maintenance()
    print("payout batch state:", st)
    settings(HoldDays=14)
    held = []
    for i in range(2):
        inv, _ = checkout(store, offering, plan, ref=CODE, new_email=f"m7held{i}{ts}@example.com"); pay(store, inv); held.append(inv)
    for inv in held: commission(inv)
    sats = {i: commission(i)["sats"] for i in invs + held}

    ap.goto(f"{BASE}/plugins/maxiaffiliates/{store}/panel")
    stat = lambda k: int(ap.locator(f'[data-stat="{k}"]').inner_text().replace(",", ""))
    got = [stat(k) for k in ["clicks", "referrals", "sales", "earned", "paid"]]
    check("stats: link clicks, referred customers, sales, earned, paid out",
          clicks == [302, 302] and got[:4] == [2, 5, 5, sum(sats.values())] and got[4] == (sum(sats[i] for i in invs) if st == "Completed" else 0), f"{clicks} {st} {got}")
    bal = ap.locator("#maxiaff-panel-balance")
    check("balance: the 2 held sales are pending (14-day hold), nothing payable", bal.get_attribute("data-net") == "0" and f"{sats[held[0]] + sats[held[1]]:,} sats" in bal.inner_text(), bal.inner_text()[:200])
    rows = ap.locator("#maxiaff-panel-commissions tr[data-invoice]")
    expect = ["Paid"] * 3 + ["Pending"] * 2 if st == "Completed" else None
    check("commissions list shows exactly this affiliate's 5 sales with status",
          sorted(rows.evaluate_all("rs => rs.map(r => r.dataset.invoice)")) == sorted(invs + held)
          and (expect is None or sorted(rows.evaluate_all("rs => rs.map(r => r.dataset.status)")) == sorted(expect)))
    check("payout history shows the cycle payout", ap.locator("#maxiaff-panel-payouts tr[data-state]").count() == 1, str(st))
    shot(ap, "04-panel-overview.png")

    # 3) store email configured -> magic link by email (Mailpit sink)
    r = gf("PUT", f"/api/v1/stores/{store}/email", {"server": "127.0.0.1", "port": 39025, "login": "", "password": "", "from": "affiliates@cointeligencia.test", "disableCertificateCheck": True})
    mail("/api/v1/messages", "DELETE")
    ap.click("#maxiaff-panel-logout"); ap.wait_for_load_state("networkidle")
    check("sign out ends the session; login now offers email sign-in", "/panel/login" in ap.url and ap.locator("#maxiaff-panel-send").count() == 1, str(r)[:150])
    op.goto(f"{BASE}/plugins/maxiaffiliates/{store}/panel"); check("other device still signed in after sign-out here", op.locator("#maxiaff-panel").count() == 1)
    shot(ap, "05-panel-login-email.png")
    ap.fill("#email", "nobody" + AFF); ap.click("#maxiaff-panel-send"); ap.wait_for_load_state("networkidle")
    unknown_page = ap.inner_text("#maxiaff-panel-sent")
    ap.goto(panel_login); ap.fill("#email", AFF.upper()); ap.click("#maxiaff-panel-send"); ap.wait_for_load_state("networkidle")
    check("same 'check your email' answer for unknown and known addresses", unknown_page == ap.inner_text("#maxiaff-panel-sent"))
    shot(ap, "06-panel-login-sent.png")
    msgs = []
    for _ in range(20):
        msgs = inbox(AFF)
        if msgs: break
        time.sleep(1)
    check("exactly one email, only to the known affiliate", len(msgs) == 1 and len(mail("/api/v1/messages")["messages"]) == 1)
    body = mail(f"/api/v1/message/{msgs[0]['ID']}")["HTML"] if msgs else ""
    m = re.search(r'href="([^"]+/panel/auth\?token=[A-Za-z0-9_-]{43})"', body)
    check("email contains a sign-in link", m is not None)
    mp = b.new_context(viewport={"width": 1000, "height": 700}).new_page()
    if msgs:
        mp.goto(f"{MAIL}/view/{msgs[0]['ID']}"); mp.wait_for_timeout(1500)
        mp.evaluate("""() => { for (const f of document.querySelectorAll('iframe')) { const d = f.contentDocument; if (d) for (const a of d.querySelectorAll('a')) { a.href = a.href.replace(/token=[^&]+/, 'token=masked'); } } }""")
        mp.screenshot(path=str(SHOTS / "07-magic-link-email.png"))
    magic = m.group(1).replace("&amp;", "&") if m else BASE
    ap.goto(magic); ap.click("#maxiaff-panel-continue"); ap.wait_for_load_state("networkidle")
    check("magic link signs in", ap.locator("#maxiaff-panel").count() == 1)
    z = b.new_context().new_page(); z.goto(magic); z.click("#maxiaff-panel-continue"); z.wait_for_load_state("networkidle")
    check("magic link is single use", z.locator("#maxiaff-panel-error").count() == 1)
    shot(z, "08-magic-link-used.png")

    # 4) payout address update
    ap.check("#pm-oc"); ap.fill("#maxiaff-panel-dest", "bc1qw508d6qejxtdg4y5r3zarvary0c5xw7kemeawh"); ap.click("#maxiaff-panel-save"); ap.wait_for_load_state("networkidle")
    check("mainnet address rejected on regtest", ap.locator("#maxiaff-panel-err").count() == 1)
    shot(ap, "09-payout-address-invalid.png")
    mail("/api/v1/messages", "DELETE")
    ap.fill("#maxiaff-panel-dest", ADDR2); ap.click("#maxiaff-panel-save"); ap.wait_for_load_state("networkidle")
    saved = h.psql(f'select "PayoutDestination" from {S}."Affiliates" where "Email" = \'{AFF}\'')
    check("new address saved", ap.locator("#maxiaff-panel-ok").count() == 1 and saved == ADDR2)
    shot(ap, "10-payout-address-saved.png")
    for _ in range(15):
        if inbox(AFF): break
        time.sleep(1)
    check("change notification emailed", any("payout address was changed" in x["Subject"] for x in inbox(AFF)))
    op.goto(f"{BASE}/plugins/maxiaffiliates/{store}/panel")
    check("address change signs out the other device", "/panel/login" in op.url)
    zz = b.new_context().new_page(); zz.goto(link); zz.click("#maxiaff-panel-continue"); zz.wait_for_load_state("networkidle")
    check("and revokes the old admin link", zz.locator("#maxiaff-panel-error").count() == 1)
    pg.goto(adm); check("admin list shows the new address", ADDR2 in pg.locator(f'tr[data-affiliate="{AFF}"]').inner_text())

    # 5) admin reports (BTCPay Reporting page; CSV export built in)
    rep = gf("POST", f"/api/v1/stores/{store}/reports", {"search": "view:Affiliate commissions,daterange:alltime,timezone:UTC"})
    names = [f["name"] for f in rep.get("fields", [])] if isinstance(rep, dict) else []
    data = rep.get("data", []) if isinstance(rep, dict) else []
    ii = names.index("InvoiceId") if "InvoiceId" in names else 0
    check("'Affiliate commissions' report lists this affiliate's 5 commissions", {r[ii] for r in data} >= set(invs + held), str(rep)[:200])
    rp = gf("POST", f"/api/v1/stores/{store}/reports", {"search": "view:Affiliate payouts,daterange:alltime,timezone:UTC"})
    check("'Affiliate payouts' report lists the payout", isinstance(rp, dict) and any(AFF in r for r in rp.get("data", [])), str(rp)[:200])
    for view, fname in [("Affiliate commissions", "11-admin-report-commissions.png"), ("Affiliate payouts", "12-admin-report-payouts.png")]:
        pg.goto(f"{BASE}/stores/{store}/reports?viewName={urllib.parse.quote(view)}"); pg.wait_for_timeout(2500)
        shot(pg, fname, full=False)

    gf("PUT", f"/api/v1/stores/{store}/email", NO_EMAIL)
    b.close()

ok = sum(1 for _, r in results if r); print(f"\n{ok}/{len(results)} checks passed")
sys.exit(0 if ok == len(results) else 1)
