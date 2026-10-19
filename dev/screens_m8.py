"""M8: Spanish screenshots. Selects BTCPay's "Spanish" dictionary (seeded by the plugin), captures admin + panel screens,
checks a few strings, then switches the server back to English. Mailpit must run (--profile mail). Never prints secrets."""
import json, pathlib, re, sys, time, urllib.request
sys.path.insert(0, str(pathlib.Path(__file__).parent))
from playwright.sync_api import sync_playwright
import acceptance_m3_helpers as h
from acceptance_m4 import BASE, ROOT, email, pw, gf, S

MAIL = "http://127.0.0.1:39026"
SHOTS = ROOT / "docs/screens/m8"; SHOTS.mkdir(parents=True, exist_ok=True)
results = []
def check(name, cond, extra=""):
    results.append((name, bool(cond))); print(("PASS " if cond else "FAIL ") + name + (f"  [{extra}]" if extra and not cond else ""))
def mail(path, method="GET"):
    with urllib.request.urlopen(urllib.request.Request(MAIL + path, method=method)) as r:
        t = r.read(); return json.loads(t) if method == "GET" else None

store = gf("GET", "/api/v1/stores")[0]["id"]
aff = h.psql(f"select \"Id\"||'|'||\"Email\" from {S}.\"Affiliates\" where \"StoreId\"='{store}' and \"Status\"='Active' and \"Email\" like 'm7aff%' order by \"CreatedAt\" desc limit 1").split("|")
AFF_ID, AFF = aff
with sync_playwright() as p:
    b = p.chromium.launch(channel="chrome", headless=True, args=["--no-sandbox", "--disable-dev-shm-usage"])
    pg = b.new_context(viewport={"width": 1280, "height": 900}).new_page()
    pg.goto(f"{BASE}/login"); pg.fill("#Email", email); pg.fill("#Password", pw); pg.click("#LoginButton"); pg.wait_for_load_state("networkidle")
    def lang(name):
        pg.goto(f"{BASE}/server/translations")
        pg.locator(f'form[action$="/server/translations/{name}/select"] button').click(); pg.wait_for_load_state("networkidle")
    pg.goto(f"{BASE}/server/translations")
    check("plugin created a 'Spanish' dictionary in Server Settings → Translations", pg.locator('form[action$="/server/translations/Spanish/select"]').count() == 1)
    n = int(h.psql("select count(*) from lang_translations where dict_id='Spanish'"))
    check("Spanish dictionary holds the plugin strings", n >= 240, str(n))
    lang("Spanish")
    shots = [("01-admin-afiliados.png", f"/stores/{store}/plugins/maxiaffiliates/affiliates", "Enlaces de referido"),
             ("02-admin-configuracion.png", f"/stores/{store}/plugins/maxiaffiliates/settings", "Periodo de retención"),
             ("03-admin-comisiones.png", f"/stores/{store}/plugins/maxiaffiliates/commissions", "Saldos"),
             ("04-admin-pagos.png", f"/stores/{store}/plugins/maxiaffiliates/payouts", "Ciclos de pago"),
             ("05-registro-afiliado.png", f"/plugins/maxiaffiliates/{store}/signup", "Hazte afiliado")]
    for fn, path, needle in shots:
        pg.goto(BASE + path); pg.wait_for_load_state("networkidle")
        check(f"{fn}: shows Spanish ('{needle}')", needle in pg.inner_text("body"))
        pg.screenshot(path=str(SHOTS / fn), full_page=fn != "01-admin-afiliados.png")
    cyc = pg.goto(f"{BASE}/stores/{store}/plugins/maxiaffiliates/payouts") and pg.locator("#maxiaff-cycles a").first
    if cyc.count():
        cyc.click(); pg.wait_for_load_state("networkidle"); pg.screenshot(path=str(SHOTS / "06-admin-informe-ciclo.png"), full_page=True)
        check("cycle report in Spanish", "Comisiones por pagar" in pg.inner_text("body") or "Informe" in pg.inner_text("body"))

    # panel via email magic link (store SMTP -> Mailpit)
    gf("PUT", f"/api/v1/stores/{store}/email", {"server": "127.0.0.1", "port": 39025, "login": "", "password": "", "from": "affiliates@cointeligencia.test", "disableCertificateCheck": True})
    mail("/api/v1/messages", "DELETE")
    a = b.new_context(viewport={"width": 1280, "height": 900}); ap = a.new_page()
    ap.goto(f"{BASE}/plugins/maxiaffiliates/{store}/panel/login")
    check("panel login in Spanish", "Enviarme un enlace de acceso" in ap.inner_text("body"))
    ap.screenshot(path=str(SHOTS / "07-panel-acceso.png"), full_page=True)
    ap.fill("#email", AFF); ap.click("#maxiaff-panel-send"); ap.wait_for_load_state("networkidle")
    ap.screenshot(path=str(SHOTS / "08-panel-revisa-tu-correo.png"), full_page=True)
    msgs = []
    for _ in range(20):
        msgs = mail("/api/v1/messages")["messages"]
        if msgs: break
        time.sleep(1)
    check("email subject in Spanish", bool(msgs) and msgs[0]["Subject"].startswith("Tu enlace para entrar"), msgs[0]["Subject"] if msgs else "")
    html = mail(f"/api/v1/message/{msgs[0]['ID']}")["HTML"] if msgs else ""
    check("email body in Spanish", "Entrar al panel de afiliados" in html)
    mp = b.new_context(viewport={"width": 1000, "height": 650}).new_page()
    if msgs:
        mp.goto(f"{MAIL}/view/{msgs[0]['ID']}"); mp.wait_for_timeout(1500); mp.screenshot(path=str(SHOTS / "09-correo-enlace-magico.png"))
    link = re.search(r'href="([^"]+/panel/auth\?token=[^"]+)"', html)
    if link:
        ap.goto(link.group(1).replace("&amp;", "&")); ap.click("#maxiaff-panel-continue"); ap.wait_for_load_state("networkidle")
    body = ap.inner_text("body")
    check("panel in Spanish (stats, balance, history, address)", all(s in body for s in ["Tu enlace de referido", "Clics en el enlace", "Saldo", "Historial de pagos", "Dirección de pago", "Pagado"]))
    ap.screenshot(path=str(SHOTS / "10-panel-afiliado.png"), full_page=True)
    ap.fill("#maxiaff-panel-dest", "bc1qw508d6qejxtdg4y5r3zarvary0c5xw7kemeawh"); ap.click("#maxiaff-panel-save"); ap.wait_for_load_state("networkidle")
    check("validation error in Spanish", "Ingresa una dirección de Bitcoin válida" in ap.inner_text("#maxiaff-panel-err"))
    ap.screenshot(path=str(SHOTS / "11-panel-error-direccion.png"))
    ap.click("#maxiaff-panel-logout")

    gf("PUT", f"/api/v1/stores/{store}/email", {"server": "", "port": None, "login": "", "password": "", "from": "", "disableCertificateCheck": False})
    lang("English")
    pg.goto(f"{BASE}/stores/{store}/plugins/maxiaffiliates/affiliates")
    check("switched back to English", "Referral links" in pg.inner_text("body"))
    b.close()
ok = sum(1 for _, r in results if r); print(f"\n{ok}/{len(results)} checks passed"); sys.exit(0 if ok == len(results) else 1)
