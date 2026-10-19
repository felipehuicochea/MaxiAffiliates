"""rpc/psql helpers for dev acceptance scripts (regtest only; never prints credentials)."""
import base64, json, subprocess, urllib.request, urllib.error

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

