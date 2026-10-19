"""M2 acceptance walk-through + screenshots (regtest dev server). Reads dev admin creds from the gitignored file; never prints them."""
import pathlib, re, sys
from playwright.sync_api import sync_playwright, expect
BASE = "http://127.0.0.1:14142"
ROOT = pathlib.Path(__file__).resolve().parents[1]
OUT = ROOT / "docs/screens/m2"
email, pw = (ROOT / "dev/.btcpay-data/dev-admin.txt").read_text().split()[:2]
REGTEST_ADDR = "bcrt1qw508d6qejxtdg4y5r3zarvary0c5xw7kygt080"
results = []
def check(name, cond):
    results.append((name, bool(cond))); print(("PASS " if cond else "FAIL ") + name)

with sync_playwright() as p:
    b = p.chromium.launch(channel="chrome", headless=True, args=["--no-sandbox", "--disable-dev-shm-usage"])
    admin = b.new_context(viewport={"width": 1280, "height": 860})
    pg = admin.new_page()
    pg.goto(f"{BASE}/login"); pg.fill("#Email", email); pg.fill("#Password", pw); pg.click("#LoginButton")
    pg.wait_for_load_state("networkidle")
    # store
    pg.goto(f"{BASE}/stores/create")
    if pg.locator("#Name").count():
        pg.fill("#Name", "Cointeligencia (regtest)"); pg.click("#Create"); pg.wait_for_load_state("networkidle")
    m = re.search(r"/stores/([^/]+)", pg.url)
    if not m:
        pg.goto(BASE); m = re.search(r"/stores/([^/]+)", pg.url)
    store = m.group(1)
    pg.goto(f"{BASE}/stores/{store}/plugins/maxiaffiliates/affiliates")
    check("admin list reachable and nav link present", pg.locator("#menu-maxiaffiliates").count() == 1)
    signup_url = pg.locator("#maxiaff-signup-url").inner_text()

    anon = b.new_context(viewport={"width": 1100, "height": 860}).new_page()
    anon.goto(signup_url); anon.screenshot(path=str(OUT / "01-signup-form.png"), full_page=True)
    anon.fill("#Email", "bad-address@example.com"); anon.check("#pm-oc"); anon.fill("#PayoutDestination", "bc1qnotvalid"); anon.click("#maxiaff-submit")
    err = anon.locator(".text-danger").all_inner_texts()
    check("invalid on-chain address shows validation error", any("Bitcoin address" in e for e in err))
    anon.screenshot(path=str(OUT / "02-signup-validation-error.png"), full_page=True)
    for i, (mail, method, dest) in enumerate([("ana@example.com", "#pm-ln", "ana@walletofsatoshi.com"),
                                               ("bruno@example.com", "#pm-oc", REGTEST_ADDR),
                                               ("spam@example.com", "#pm-ln", "spam@ln.example")]):
        anon.goto(signup_url); anon.fill("#Email", mail); anon.fill("#Name", mail.split("@")[0].title()); anon.check(method); anon.fill("#PayoutDestination", dest); anon.click("#maxiaff-submit")
        ok = anon.locator("#maxiaff-signup-done").count() == 1 and anon.locator("#maxiaff-signup-done").get_attribute("data-status") == "Pending"
        check(f"signup {mail} -> Pending", ok)
        if i == 0: anon.screenshot(path=str(OUT / "03-signup-done-pending.png"), full_page=True)
    anon.goto(signup_url); anon.fill("#Email", "ANA@example.com"); anon.fill("#PayoutDestination", "x@y.example"); anon.click("#maxiaff-submit")
    check("duplicate email rejected", any("already exists" in e for e in anon.locator(".text-danger").all_inner_texts()))

    pg.goto(f"{BASE}/stores/{store}/plugins/maxiaffiliates/affiliates")
    check("admin list shows 3 affiliates", pg.locator("#maxiaff-affiliates tbody tr").count() == 3)
    pg.screenshot(path=str(OUT / "04-admin-list-pending.png"), full_page=True)
    def act(mail, action):
        pg.locator(f'tr[data-affiliate="{mail}"] button[data-action="{action}"]').click(); pg.wait_for_load_state("networkidle")
    def status(mail): return pg.locator(f'tr[data-affiliate="{mail}"] .badge').inner_text()
    act("ana@example.com", "Approve"); check("approve -> Active", status("ana@example.com") == "Active")
    pg.screenshot(path=str(OUT / "05-admin-approved.png"), full_page=True)
    act("bruno@example.com", "Approve"); act("bruno@example.com", "Suspend"); check("suspend -> Suspended", status("bruno@example.com") == "Suspended")
    pg.screenshot(path=str(OUT / "06-admin-suspended.png"), full_page=True)
    pg.once("dialog", lambda d: d.accept())
    act("spam@example.com", "Ban"); check("ban -> Banned (after confirm)", status("spam@example.com") == "Banned")
    check("banned row has no actions", pg.locator('tr[data-affiliate="spam@example.com"] button').count() == 0)
    pg.screenshot(path=str(OUT / "07-admin-banned.png"), full_page=True)
    pg.goto(f"{BASE}/stores/{store}/plugins/maxiaffiliates/affiliates?status=Active")
    check("status filter", pg.locator("#maxiaff-affiliates tbody tr").count() == 1)
    pg.screenshot(path=str(OUT / "08-admin-filter-active.png"), full_page=True)

    # authz: anonymous cannot see the admin list
    r = anon.request.get(f"{BASE}/stores/{store}/plugins/maxiaffiliates/affiliates", max_redirects=0)
    check("anonymous admin list -> redirect to login", r.status in (302, 401, 403) or "login" in (r.headers.get("location") or ""))
    r = anon.request.post(f"{BASE}/stores/{store}/plugins/maxiaffiliates/affiliates/x/Approve", max_redirects=0)
    check("anonymous action POST rejected", r.status in (302, 400, 401, 403))
    r = anon.request.get(f"{BASE}/plugins/maxiaffiliates/nonexistentstore/signup")
    check("unknown store signup -> 404", r.status == 404)
    b.close()
fails = [n for n, ok in results if not ok]
print(f"{len(results)-len(fails)}/{len(results)} checks passed")
sys.exit(1 if fails else 0)
