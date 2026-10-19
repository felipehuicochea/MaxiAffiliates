"""M3 acceptance on the regtest dev server: referral links, website helper, checkout carrying the ref, and a paid regtest invoice.
Never prints secrets (admin password, wallet mnemonic, RPC creds)."""
import base64, http.server, json, pathlib, re, subprocess, sys, threading, time, urllib.request
from playwright.sync_api import sync_playwright
BASE = "http://127.0.0.1:14142"; LANDING_PORT = 8099
ROOT = pathlib.Path(__file__).resolve().parents[1]
email, pw = (ROOT / "dev/.btcpay-data/dev-admin.txt").read_text().split()[:2]
AUTH = "token " + (ROOT / "dev/.btcpay-data/api-key.txt").read_text().strip()  # run dev/make_api_key.py once
results = []
def check(name, cond, extra=""):
    results.append((name, bool(cond))); print(("PASS " if cond else "FAIL ") + name + (f"  [{extra}]" if extra and not cond else ""))

def gf(method, path, body=None):
    req = urllib.request.Request(BASE + path, method=method, data=json.dumps(body).encode() if body is not None else None,
                                 headers={"Authorization": AUTH, "Content-Type": "application/json"})
    with urllib.request.urlopen(req) as r: return json.loads(r.read() or b"null")

def rpc(method, *params, wallet=None):
    url = "http://127.0.0.1:43782/" + (f"wallet/{wallet}" if wallet else "")
    req = urllib.request.Request(url, data=json.dumps({"jsonrpc": "1.0", "id": 1, "method": method, "params": list(params)}).encode(),
                                 headers={"Authorization": "Basic " + base64.b64encode(b"ceiwHEbqWI83:DwubwWsoo3").decode()})
    try:
        with urllib.request.urlopen(req) as r: return json.loads(r.read())["result"]
    except urllib.error.HTTPError as e:
        return {"error": json.loads(e.read()).get("error")}

def psql(sql):
    return subprocess.run(["sudo", "docker", "exec", "maxiaff-regtest-postgres-1", "psql", "-h", "127.0.0.1", "-p", "39372", "-U", "postgres", "-d", "btcpayserver", "-Atc", sql],
                          capture_output=True, text=True).stdout.strip()

store = gf("GET", "/api/v1/stores")[0]["id"]
off = gf("POST", f"/api/v1/stores/{store}/offerings", {"appName": f"Cointeligencia M3 {int(time.time())}"})
offering = off.get("id") or off.get("offeringId")
plan = gf("POST", f"/api/v1/stores/{store}/offerings/{offering}/plans",
          {"name": "Pro Monthly", "price": "10", "currency": "USD", "recurringType": "Monthly", "optimisticActivation": False})["id"]
check("offering + plan created via Greenfield", offering and plan)

# landing page server
html = (ROOT / "dev/landing/index.html").read_text().replace("__STORE__", store).replace("__OFFERING__", offering).replace("__PLAN__", plan)
class H(http.server.BaseHTTPRequestHandler):
    def do_GET(self):
        self.send_response(200); self.send_header("Content-Type", "text/html"); self.end_headers(); self.wfile.write(html.encode())
    def log_message(self, *a): pass
srv = http.server.ThreadingHTTPServer(("127.0.0.1", LANDING_PORT), H); threading.Thread(target=srv.serve_forever, daemon=True).start()
LANDING = f"http://127.0.0.1:{LANDING_PORT}/"

with sync_playwright() as p:
    b = p.chromium.launch(channel="chrome", headless=True, args=["--no-sandbox", "--disable-dev-shm-usage"])
    adm = b.new_context(viewport={"width": 1280, "height": 900}); pg = adm.new_page()
    pg.goto(f"{BASE}/login"); pg.fill("#Email", email); pg.fill("#Password", pw); pg.click("#LoginButton"); pg.wait_for_load_state("networkidle")
    pg.goto(f"{BASE}/stores/{store}/plugins/maxiaffiliates/affiliates")
    pg.fill("#landingUrl", LANDING); pg.click("#maxiaff-save-landing"); pg.wait_for_load_state("networkidle")
    check("landing URL saved via admin UI", pg.locator("#landingUrl").input_value() == LANDING)
    pg.screenshot(path=str(ROOT / "docs/screens/m3/01-admin-referral-settings.png"), full_page=True)
    # make sure there are two active affiliates: ana (from M2) + carla
    anon = b.new_context().new_page()
    anon.goto(pg.locator("#maxiaff-signup-url").inner_text())
    if anon.locator("#Email").count():
        anon.fill("#Email", "carla@example.com"); anon.fill("#PayoutDestination", "carla@ln.example"); anon.click("#maxiaff-submit")
    pg.reload()
    if pg.locator('tr[data-affiliate="carla@example.com"] button[data-action="Approve"]').count():
        pg.locator('tr[data-affiliate="carla@example.com"] button[data-action="Approve"]').click(); pg.wait_for_load_state("networkidle")
    code = lambda m: pg.locator(f'tr[data-affiliate="{m}"] code').inner_text()
    ANA, CARLA, BRUNO, SPAM = code("ana@example.com"), code("carla@example.com"), code("bruno@example.com"), code("spam@example.com")

    # 1) link click -> cookie + redirect with ?ref
    v = b.new_context(); rq = v.request
    r = rq.get(f"{BASE}/aff/{store}/{ANA}", max_redirects=0)
    loc = r.headers.get("location", "")
    check("referral link 302 -> landing?ref=CODE", r.status == 302 and loc == f"{LANDING}?ref={ANA}", f"{r.status} {loc}")
    cookies = {c["name"]: c for c in v.cookies()}
    ck = cookies.get(f"maxiaff_ref_{store}")
    check("first-party cookie set, HttpOnly, ~30 days", ck and ck["httpOnly"] and ck["value"].startswith(ANA + ".") and 29*86400 < ck["expires"] - time.time() <= 30*86400 + 5)
    r = rq.get(f"{BASE}/aff/{store}/{CARLA}", max_redirects=0)
    check("second click keeps first-click code", r.headers.get("location") == f"{LANDING}?ref={ANA}" and {c['name']: c for c in v.cookies()}[f"maxiaff_ref_{store}"]["value"] == ck["value"])
    r = v.request.post(f"{BASE}/plugins/maxiaffiliates/{store}/checkout", data=json.dumps({"offeringId": offering, "planId": plan, "ref": CARLA}), headers={"Content-Type": "application/json"})
    j = r.json()
    co = gf("GET", f"/api/v1/plan-checkout/{j['checkoutId']}")
    check("checkout: BTCPay cookie (first click) beats explicit ref", j["attributed"] and co["invoiceMetadata"].get("maxiaffRef") == ANA and co["newSubscriberMetadata"].get("maxiaffRef") == ANA)
    for bad, label in [(BRUNO, "suspended"), (SPAM, "banned"), ("NOPE2345", "unknown")]:
        x = b.new_context().request
        r = x.get(f"{BASE}/aff/{store}/{bad}", max_redirects=0)
        check(f"{label} code: redirect without ref, no cookie", r.status == 302 and r.headers.get("location") == LANDING and not x.storage_state()["cookies"])
    x = b.new_context().request
    j = x.post(f"{BASE}/plugins/maxiaffiliates/{store}/checkout", data=json.dumps({"offeringId": offering, "planId": plan, "ref": CARLA, "email": "Carla@Example.com"}), headers={"Content-Type": "application/json"}).json()
    co = gf("GET", f"/api/v1/plan-checkout/{j['checkoutId']}")
    check("self-referral (buyer email == affiliate email) not attributed", not j["attributed"] and "maxiaffRef" not in co["invoiceMetadata"])
    r = x.get(f"{BASE}/plugins/maxiaffiliates/{store}/checkout?offeringId={offering}&planId={plan}&ref={CARLA}", max_redirects=0)
    check("no-JS checkout link 302 -> /plan-checkout/", r.status == 302 and "/plan-checkout/" in r.headers.get("location", ""))
    check("unknown plan -> 404", x.post(f"{BASE}/plugins/maxiaffiliates/{store}/checkout", data=json.dumps({"offeringId": offering, "planId": "nope"}), headers={"Content-Type": "application/json"}).status == 404)
    r = x.fetch(f"{BASE}/plugins/maxiaffiliates/{store}/checkout", method="OPTIONS", headers={"Origin": f"http://127.0.0.1:{LANDING_PORT}", "Access-Control-Request-Method": "POST"})
    check("CORS preflight OK for the website (BTCPay global policy)", r.status == 204 and r.headers.get("access-control-allow-origin") in ("*", f"http://127.0.0.1:{LANDING_PORT}"))

    # 2) full browser flow through the website helper
    bc = b.new_context(viewport={"width": 1100, "height": 860}); page = bc.new_page()
    page.on("console", lambda m: print("  console:", m.type, m.text[:200]) if m.type in ("error", "warning") else None)
    page.on("response", lambda r: print("  resp:", r.status, r.url[:120]) if "/checkout" in r.url else None)
    page.goto(f"{BASE}/aff/{store}/{CARLA}"); page.wait_for_load_state("networkidle")
    check("browser lands on landing page with helper ref", page.url == f"{LANDING}?ref={CARLA}" and page.locator("#ref").inner_text() == CARLA)
    page.screenshot(path=str(ROOT / "docs/screens/m3/02-landing-with-ref.png"))
    page.goto(f"{LANDING}?ref={ANA}")
    check("helper keeps first click (localStorage) on later ?ref", page.evaluate("MaxiAffiliates.getRef()") == CARLA)
    with page.expect_navigation(url=re.compile(r".*/plan-checkout/.*")):
        page.click("#subscribe")
    checkout_id = page.url.rstrip("/").split("/plan-checkout/")[1].split("?")[0]
    co = gf("GET", f"/api/v1/plan-checkout/{checkout_id}")
    check("helper button -> plan checkout carrying ref", co["invoiceMetadata"].get("maxiaffRef") == CARLA and co["newSubscriberMetadata"].get("maxiaffRef") == CARLA)
    page.screenshot(path=str(ROOT / "docs/screens/m3/03-plan-checkout.png"))

    # 3) pay it on regtest -> invoice + subscriber metadata
    gf("POST", f"/api/v1/stores/{store}/payment-methods/BTC-CHAIN/wallet/generate", {"savePrivateKeys": False, "importKeysToRPC": False, "wordCount": 12, "scriptPubKeyType": "Segwit"}) \
        if not gf("GET", f"/api/v1/stores/{store}/payment-methods?includeConfig=false") else None
    page.get_by_placeholder("Enter your email address").fill("buyer1@example.com")
    with page.expect_navigation():
        page.get_by_role("button", name="Proceed to Secure Payment").click()
    page.wait_for_load_state("networkidle")
    m = re.search(r"/i/([^/?#]+)", page.url)
    inv_id = gf("GET", f"/api/v1/plan-checkout/{checkout_id}").get("invoiceId") or (m.group(1) if m else None)
    print("  after submit:", page.url[:90])
    inv = gf("GET", f"/api/v1/stores/{store}/invoices/{inv_id}") if inv_id else {}
    check("invoice metadata carries maxiaffRef", inv.get("metadata", {}).get("maxiaffRef") == CARLA, str(inv.get("metadata")))
    pm = [m for m in gf("GET", f"/api/v1/stores/{store}/invoices/{inv_id}/payment-methods") if m["paymentMethodId"] == "BTC-CHAIN"][0]
    rpc("createwallet", "maxiaff") if "error" in (rpc("loadwallet", "maxiaff") or {}) else None
    addr = rpc("getnewaddress", wallet="maxiaff")
    if rpc("getbalance", wallet="maxiaff") < 1: rpc("generatetoaddress", 101, addr)
    rpc("sendtoaddress", pm["destination"], pm["due"], wallet="maxiaff"); rpc("generatetoaddress", 2, addr)
    status = None
    for _ in range(60):
        status = gf("GET", f"/api/v1/stores/{store}/invoices/{inv_id}")["status"]
        if status == "Settled": break
        time.sleep(2)
    check("regtest payment settles invoice", status == "Settled", status)
    meta = ""
    for _ in range(30):
        meta = psql("select s.metadata from subs_subscribers s join subs_plan_checkouts c on c.subscriber_id = s.id where c.id = '%s'" % checkout_id.replace("'", ""))
        if meta: break
        time.sleep(2)
    check("subscriber created with maxiaffRef in its metadata", CARLA in meta, meta)
    b.close()
srv.shutdown()
fails = [n for n, ok in results if not ok]
print(f"{len(results)-len(fails)}/{len(results)} checks passed"); sys.exit(1 if fails else 0)
