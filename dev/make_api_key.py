"""Creates an unrestricted Greenfield API key for the regtest dev admin and stores it in dev/.btcpay-data/api-key.txt (gitignored). Never prints it."""
import pathlib, re
from playwright.sync_api import sync_playwright
BASE = "http://127.0.0.1:14142"; ROOT = pathlib.Path(__file__).resolve().parents[1]
email, pw = (ROOT / "dev/.btcpay-data/dev-admin.txt").read_text().split()[:2]
with sync_playwright() as p:
    b = p.chromium.launch(channel="chrome", headless=True, args=["--no-sandbox"]); pg = b.new_page()
    pg.goto(f"{BASE}/login"); pg.fill("#Email", email); pg.fill("#Password", pw); pg.click("#LoginButton"); pg.wait_for_load_state("networkidle")
    pg.goto(f"{BASE}/api-keys/authorize?permissions=unrestricted&applicationName=maxiaff-dev&strict=true&selectiveStores=false")
    pg.locator("#consent-yes, button[value='Authorize'], button:has-text('Authorize')").first.click(); pg.wait_for_load_state("networkidle")
    m = re.search(r"\b([0-9a-f]{40})\b", pg.content())
    if not m: raise SystemExit("could not find generated key on page: " + pg.url)
    f = ROOT / "dev/.btcpay-data/api-key.txt"; f.write_text(m.group(1)); f.chmod(0o600)
    print("api key stored at", f)
    b.close()
