"""Regtest Lightning for dev: merchant CLN (the store's node) opens a channel to customer CLN (the affiliate's wallet).
Requires: docker compose --profile ln up -d. Regtest only; prints no credentials."""
import json, subprocess, sys, time
sys.path.insert(0, __file__.rsplit("/", 1)[0])
import acceptance_m3_helpers as h

def cli(node, *args):
    out = subprocess.run(["sudo", "docker", "exec", f"maxiaff-regtest-{node}_lightningd-1", "lightning-cli", "--network=regtest", *args],
                         capture_output=True, text=True)
    return json.loads(out.stdout) if out.stdout.strip().startswith(("{", "[")) else {"error": out.stderr.strip() or out.stdout.strip()}

def main():
    subprocess.run(["sudo", "chmod", "666", "dev/.ln/merchant/lightning-rpc", "dev/.ln/customer/lightning-rpc"], cwd=__file__.rsplit("/", 2)[0], check=True)
    chans = cli("merchant", "listpeerchannels").get("channels", [])
    if any(c["state"] == "CHANNELD_NORMAL" for c in chans):
        print("channel already open"); return
    h.rpc("createwallet", "maxiaff") if "error" in (h.rpc("loadwallet", "maxiaff") or {}) else None
    mine = h.rpc("getnewaddress", wallet="maxiaff")
    if h.rpc("getbalance", wallet="maxiaff") < 1: h.rpc("generatetoaddress", 101, mine)
    na = cli("merchant", "newaddr"); addr = na.get("bech32") or na["p2tr"]
    h.rpc("sendtoaddress", addr, 0.1, wallet="maxiaff"); h.rpc("generatetoaddress", 6, mine)
    for _ in range(60):
        if cli("merchant", "listfunds").get("outputs"): break
        time.sleep(1)
    cust = cli("customer", "getinfo")["id"]
    print("connect:", "error" not in cli("merchant", "connect", f"{cust}@127.0.0.1:39736"))
    r = cli("merchant", "fundchannel", cust, "2000000")
    print("fundchannel:", "error" not in r, r.get("error", "")[:200])
    for _ in range(90):
        h.rpc("generatetoaddress", 1, mine)
        st = [c["state"] for c in cli("merchant", "listpeerchannels").get("channels", [])]
        if "CHANNELD_NORMAL" in st: print("channel normal"); return
        time.sleep(2)
    print("channel not normal:", st); sys.exit(1)

if __name__ == "__main__":
    main()
