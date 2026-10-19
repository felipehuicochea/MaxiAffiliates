"""Installs dist/*.btcpay on a FRESH BTCPay 2.4.5 (dev/run-btcpay-fresh.sh, port 14160) through Server Settings → Plugins → Upload,
then (after the caller restarts BTCPay) verifies it loaded. Usage: fresh_install.py upload | verify. Never prints the password."""
import glob, json, pathlib, secrets, sys, urllib.request
from playwright.sync_api import sync_playwright
ROOT = pathlib.Path(__file__).resolve().parent.parent
BASE = "http://127.0.0.1:14160"; CRED = ROOT / "dev/.btcpay-fresh/admin.txt"
SHOTS = ROOT / "docs/screens/m9"; SHOTS.mkdir(parents=True, exist_ok=True)
PKG = sorted(glob.glob(str(ROOT / "dist/BTCPayServer.Plugins.MaxiAffiliates/*/*.btcpay")))[-1]
results = []
def check(n, c, x=""): results.append(bool(c)); print(("PASS " if c else "FAIL ") + n + (f" [{x}]" if x and not c else ""))
mode = sys.argv[1]
with sync_playwright() as p:
    b = p.chromium.launch(channel="chrome", headless=True, args=["--no-sandbox", "--disable-dev-shm-usage"])
    pg = b.new_context(viewport={"width": 1280, "height": 900}).new_page()
    if mode == "upload":
        if not CRED.exists():
            email, pw = "admin@fresh.test", secrets.token_urlsafe(18)
            CRED.write_text(json.dumps({"email": email, "password": pw})); CRED.chmod(0o600)
            pg.goto(f"{BASE}/register"); pg.fill("#Email", email); pg.fill("#Password", pw); pg.fill("#ConfirmPassword", pw)
            pg.click("#RegisterButton"); pg.wait_for_load_state("networkidle")
        else:
            c = json.loads(CRED.read_text())
            pg.goto(f"{BASE}/login"); pg.fill("#Email", c["email"]); pg.fill("#Password", c["password"]); pg.click("#LoginButton"); pg.wait_for_load_state("networkidle")
        check("fresh server: first user registered as admin", "/login" not in pg.url and "/register" not in pg.url, pg.url)
        pg.goto(f"{BASE}/server/plugins")
        check("plugin not installed yet", "MaxiAffiliates" not in pg.inner_text("body"))
        pg.set_input_files("#files", PKG); pg.locator('form[enctype="multipart/form-data"]').evaluate('f => f.submit()'); pg.wait_for_load_state("networkidle")
        body = pg.inner_text("body")
        check("upload accepted, restart pending", "You need to restart BTCPay Server" in body, body[:300])
        pg.screenshot(path=str(SHOTS / "01-plugin-uploaded.png"), full_page=True)
    else:
        c = json.loads(CRED.read_text())
        pg.goto(f"{BASE}/login"); pg.fill("#Email", c["email"]); pg.fill("#Password", c["password"]); pg.click("#LoginButton"); pg.wait_for_load_state("networkidle")
        h = json.loads(urllib.request.urlopen(f"{BASE}/plugins/maxiaffiliates/health").read())
        check("plugin loaded, schema migrated (all migrations applied, 0 pending)", h["migrationsApplied"] >= 6 and h["pendingMigrations"] == 0, str(h))
        pg.goto(f"{BASE}/server/plugins"); check("listed as installed in Server Settings → Plugins", "MaxiAffiliates" in pg.inner_text("body"))
        pg.screenshot(path=str(SHOTS / "02-plugin-installed.png"), full_page=True)
        pg.goto(f"{BASE}/stores/create"); pg.fill("#Name", "Fresh store"); pg.click("#Create"); pg.wait_for_load_state("networkidle")
        store = pg.url.split("/stores/")[1].split("/")[0]
        pg.goto(f"{BASE}/stores/{store}/plugins/maxiaffiliates/settings")
        check("store settings page renders", pg.locator("#maxiaff-save-settings").count() == 1)
        pg.locator("#Settings_Enabled").set_checked(True); pg.click("#maxiaff-save-settings"); pg.wait_for_load_state("networkidle")
        check("program can be enabled", pg.locator(".alert-success").count() > 0)
        pg.goto(f"{BASE}/stores/{store}/plugins/maxiaffiliates/affiliates")
        check("Affiliates admin page + store nav entry", pg.locator("#maxiaff-signup-url").count() == 1)
        pg.screenshot(path=str(SHOTS / "03-fresh-affiliates.png"))
        a = b.new_context().new_page(); a.goto(f"{BASE}/plugins/maxiaffiliates/{store}/signup")
        check("public signup page renders", a.locator("#maxiaff-signup").count() == 1)
        a.goto(f"{BASE}/plugins/maxiaffiliates/{store}/panel/login"); check("panel login renders", a.locator("#maxiaff-panel-login").count() == 1)
        pg.goto(f"{BASE}/server/translations"); check("'Spanish' dictionary seeded", "Spanish" in pg.inner_text("body"))
        # v1 order callback in a Release build: https only, private/loopback hosts refused (no dev exception compiled in)
        cb = f"{BASE}/stores/{store}/plugins/maxiaffiliates/callbacks"
        pg.goto(cb); pg.click("#maxiaff-callback-secret"); pg.wait_for_load_state("networkidle")
        check("callback page renders; secret generated", pg.locator("#maxiaff-callback-new-secret").count() == 1)
        def save(url):
            pg.goto(cb); pg.fill("#maxiaff-callback-url", url); pg.set_checked("#maxiaff-callback-enabled", True)
            pg.click("#maxiaff-callback-save"); pg.wait_for_load_state("networkidle"); return pg.inner_text("body")
        check("Release: http callback URL refused", "The callback URL must use https." in save("http://127.0.0.1:14171/cb"))
        for u in ("https://127.0.0.1/cb", "https://localhost/cb", "https://169.254.169.254/latest", "https://10.0.0.5/cb", "https://[::1]/cb"):
            check(f"Release: {u} refused (private/loopback)", "The callback URL must point to a public host." in save(u))
        check("Release: public https URL accepted", "Callback settings saved." in save("https://shop.example.com/btcpay/callback"))
        pg.screenshot(path=str(ROOT / "docs/screens/v1-callback/03-release-https-only.png"))
    b.close()
print(f"{sum(results)}/{len(results)} checks passed"); sys.exit(0 if all(results) else 1)
