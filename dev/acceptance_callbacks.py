"""v1 acceptance on regtest: seller-site order confirmation callback.
A local receiver on 127.0.0.1 verifies every HMAC-SHA256 signature (t=…,v1=… over "{t}.{body}", ±5 min window), answers 500
to the first attempt of each settled event (to exercise retry/backoff) and 200 afterwards. Checks: secret shown once, test
send, plan-checkout (subscription) payment with an affiliate, plain invoice without affiliate, retry after 500, delivery log
and manual redelivery. Never prints the secret. Needs the dev server (Debug build: http/loopback receivers allowed)."""
import hashlib, hmac, http.server, json, pathlib, sys, threading, time
sys.path.insert(0, str(pathlib.Path(__file__).parent))
from playwright.sync_api import sync_playwright
from acceptance_m4 import BASE, ROOT, email, pw, gf, checkout, pay, commission

SHOTS = ROOT / "docs/screens/v1-callback"; SHOTS.mkdir(parents=True, exist_ok=True)
PORT = 14171
results = []
def check(name, cond, extra=""):
    results.append((name, bool(cond))); print(("PASS " if cond else "FAIL ") + name + (f"  [{extra}]" if extra and not cond else ""))

SECRET = {"v": None}
received = []  # dicts: event, delivery, valid, t, at, body, json, answered
seen = {}
def verify(header, body):
    try:
        parts = dict(p.strip().split("=", 1) for p in header.split(","))
        t = int(parts["t"])
        mac = hmac.new(SECRET["v"].encode(), f"{t}.".encode() + body, hashlib.sha256).hexdigest()
        return hmac.compare_digest(mac, parts["v1"]) and abs(time.time() - t) <= 300, t
    except Exception:
        return False, None

class H(http.server.BaseHTTPRequestHandler):
    def log_message(self, *a): pass
    def do_POST(self):
        body = self.rfile.read(int(self.headers.get("Content-Length", 0)))
        ok, t = verify(self.headers.get("MaxiAff-Signature", ""), body)
        j = json.loads(body)
        ev = self.headers.get("MaxiAff-Event-Id")
        n = seen.get(ev, 0) + 1; seen[ev] = n
        status = 200 if not ok else (500 if j.get("type") == "invoice.settled" and n == 1 else 200)
        if not ok: status = 401
        received.append(dict(event=ev, delivery=self.headers.get("MaxiAff-Delivery-Id"), valid=ok, t=t, at=time.time(), body=body, json=j, answered=status))
        self.send_response(status); self.end_headers()

srv = http.server.ThreadingHTTPServer(("127.0.0.1", PORT), H)
threading.Thread(target=srv.serve_forever, daemon=True).start()
def wait_for(pred, timeout=60):
    end = time.time() + timeout
    while time.time() < end:
        r = [x for x in received if pred(x)]
        if r: return r
        time.sleep(0.5)
    return []

store = gf("GET", "/api/v1/stores")[0]["id"]
ts = int(time.time())
off = gf("POST", f"/api/v1/stores/{store}/offerings", {"appName": f"Cointeligencia CB {ts}"}); offering = off.get("id") or off.get("offeringId")
plan = gf("POST", f"/api/v1/stores/{store}/offerings/{offering}/plans", {"name": "Pro Monthly", "price": "10", "currency": "USD", "recurringType": "Monthly", "optimisticActivation": False})["id"]

with sync_playwright() as p:
    b = p.chromium.launch(channel="chrome", headless=True, args=["--no-sandbox", "--disable-dev-shm-usage"])
    pg = b.new_context(viewport={"width": 1280, "height": 1000}).new_page()
    pg.goto(f"{BASE}/login"); pg.fill("#Email", email); pg.fill("#Password", pw); pg.click("#LoginButton"); pg.wait_for_load_state("networkidle")
    pg.goto(f"{BASE}/stores/{store}/plugins/maxiaffiliates/affiliates")
    CODE = pg.locator('tr[data-affiliate="ana@example.com"] code').inner_text()
    pg.click("#maxiaff-tab-callbacks"); pg.wait_for_load_state("networkidle")
    check("Order callback tab in the plugin admin", "/plugins/maxiaffiliates/callbacks" in pg.url)

    pg.click("#maxiaff-callback-secret"); pg.wait_for_load_state("networkidle")
    SECRET["v"] = pg.locator("#maxiaff-callback-new-secret").inner_text().strip()
    check("secret generated and shown (256-bit, whsec_ prefix)", SECRET["v"].startswith("whsec_") and len(SECRET["v"]) >= 49)
    pg.goto(f"{BASE}/stores/{store}/plugins/maxiaffiliates/callbacks")
    check("secret is shown only once (gone after reload)", pg.locator("#maxiaff-callback-new-secret").count() == 0 and SECRET["v"] not in pg.content())
    pg.fill("#maxiaff-callback-url", f"http://127.0.0.1:{PORT}/btcpay/callback"); pg.set_checked("#maxiaff-callback-enabled", True)
    pg.click("#maxiaff-callback-save"); pg.wait_for_load_state("networkidle")
    check("callback URL saved and enabled", pg.locator("#maxiaff-callback-enabled").is_checked() and "Callback settings saved." in pg.inner_text("body"))

    pg.click("#maxiaff-callback-test"); pg.wait_for_load_state("networkidle")
    t = wait_for(lambda x: x["json"].get("type") == "test", 15)
    check("test-send button: receiver got a correctly signed test event", t and t[0]["valid"] and t[0]["json"]["test"] is True)
    pg.reload()
    check("test delivery logged as delivered", pg.locator('#maxiaff-callback-log tr[data-status="Succeeded"]').count() >= 1)

    # subscription (plan checkout) payment with an affiliate
    inv, co = checkout(store, offering, plan, ref=CODE, new_email=f"cb{ts}@example.com")
    check("plan checkout invoice created", inv is not None, str(co)[:200])
    check("invoice settles on regtest", pay(store, inv) == "Settled")
    first = wait_for(lambda x: x["json"].get("invoiceId") == inv, 30)
    retry = wait_for(lambda x: x["json"].get("invoiceId") == inv and x["answered"] == 200, 60)
    check("first attempt got HTTP 500 from the receiver", first and first[0]["answered"] == 500)
    check("retried after backoff (~10 s) and accepted", retry and 8 <= retry[0]["at"] - first[0]["at"] <= 30, f"{retry and retry[0]['at'] - first[0]['at']}")
    if retry:
        j = retry[0]["json"]
        check("every attempt correctly signed, fresh timestamp each time", first[0]["valid"] and retry[0]["valid"] and retry[0]["t"] > first[0]["t"])
        check("same body and event id on retry (idempotency)", first[0]["body"] == retry[0]["body"] and first[0]["event"] == retry[0]["event"] == j["eventId"] == f"evt_settled_{inv}")
        check("payload: invoice, plan, offering, subscriber, customer", j["invoiceId"] == inv and j["planId"] == plan and j["offeringId"] == offering and j["subscriberId"] and j["customerId"], json.dumps({k: j[k] for k in ("planId", "offeringId", "subscriberId")}))
        check("payload: amount, currency, status, timestamp", j["amount"] in ("10", "10.00", "10.0") and j["currency"] == "USD" and j["status"] == "settled" and abs(j["timestamp"] - time.time()) < 300, f"{j['amount']} {j['currency']}")
        com = commission(inv)
        check("payload: affiliate code and commission credited", j["affiliateCode"] == CODE and j["commissionCredited"] is True and com is not None)

    # plain invoice, no affiliate
    pinv = gf("POST", f"/api/v1/stores/{store}/invoices", {"amount": "5", "currency": "USD", "metadata": {"orderId": f"cb-{ts}"}})["id"]
    check("plain invoice settles", pay(store, pinv) == "Settled")
    pl = wait_for(lambda x: x["json"].get("invoiceId") == pinv and x["answered"] == 200, 60)
    if pl:
        j = pl[0]["json"]
        check("no-affiliate payment: affiliateCode null, not credited, no plan", j["affiliateCode"] is None and j["commissionCredited"] is False and j["planId"] is None and j["commissionOutcome"] == "no_referral")
    else: check("no-affiliate payment delivered", False)

    pg.goto(f"{BASE}/stores/{store}/plugins/maxiaffiliates/callbacks")
    row = pg.locator(f'#maxiaff-callback-log tr[data-event="evt_settled_{inv}"]').first
    check("delivery log: plan payment delivered after 2 attempts, last HTTP 200", row.get_attribute("data-status") == "Succeeded" and row.get_attribute("data-attempts") == "2" and row.get_attribute("data-http") == "200")
    pg.screenshot(path=str(SHOTS / "01-callback-settings-and-log.png"), full_page=True)
    before = len([x for x in received if x["event"] == f"evt_settled_{inv}"])
    row.locator(".maxiaff-redeliver").click(); pg.wait_for_load_state("networkidle")
    red = wait_for(lambda x: x["event"] == f"evt_settled_{inv}" and x["delivery"] != retry[0]["delivery"] and x["delivery"] != first[0]["delivery"], 15)
    check("redelivery: same event id and body, new delivery id, valid signature", red and red[0]["body"] == retry[0]["body"] and red[0]["valid"] and before == 2)
    pg.reload()
    check("redelivery shown in the log", pg.locator(f'#maxiaff-callback-log tr[data-event="evt_settled_{inv}"]').count() == 2 and pg.locator('#maxiaff-callback-log .badge:has-text("redelivery")').count() >= 1)
    pg.screenshot(path=str(SHOTS / "02-callback-redelivered.png"), full_page=True)
    check("no unsigned/invalid requests reached the receiver", all(x["valid"] for x in received))
    # leave the store with the callback disabled so later suites are unaffected
    pg.set_checked("#maxiaff-callback-enabled", False); pg.click("#maxiaff-callback-save"); pg.wait_for_load_state("networkidle")
    b.close()

srv.shutdown()
print(f"\n{sum(ok for _, ok in results)}/{len(results)} checks passed")
sys.exit(0 if all(ok for _, ok in results) else 1)
