"""Idempotent regtest setup for M6: store LN node (merchant CLN), hot on-chain wallet with funds, automated payout processors,
and an 'Affiliate wallet' store on the customer CLN exposing a Lightning address used as the LN affiliate's LNURL.
Regtest only; never prints secrets (API key, mnemonic, rune, RPC creds)."""
import pathlib, sys, time
sys.path.insert(0, str(pathlib.Path(__file__).parent))
import acceptance_m3_helpers as h
from acceptance_m4 import gf, ROOT

LN = lambda n: f"type=clightning;server=unix://{ROOT}/dev/.ln/{n}/lightning-rpc"
MARK = ROOT / "dev/.btcpay-data/m6-hotwallet.done"

def bech32_encode(hrp, data8):
    CH = "qpzry9x8gf2tvdw0s3jn54khce6mua7l"
    acc = bits = 0; data5 = []
    for b in data8:
        acc = (acc << 8) | b; bits += 8
        while bits >= 5: bits -= 5; data5.append((acc >> bits) & 31)
    if bits: data5.append((acc << (5 - bits)) & 31)
    def polymod(v):
        g = [0x3b6a57b2, 0x26508e6d, 0x1ea119fa, 0x3d4233dd, 0x2a1462b3]; chk = 1
        for x in v:
            b = chk >> 25; chk = (chk & 0x1ffffff) << 5 ^ x
            for i in range(5): chk ^= g[i] if (b >> i) & 1 else 0
        return chk
    exp = [ord(c) >> 5 for c in hrp] + [0] + [ord(c) & 31 for c in hrp]
    pm = polymod(exp + data5 + [0] * 6) ^ 1
    return hrp + "1" + "".join(CH[d] for d in data5 + [(pm >> 5 * (5 - i)) & 31 for i in range(6)])

def lnurl(url): return bech32_encode("lnurl", url.encode()).upper()

def ensure_ln(store, node):
    r = gf("PUT", f"/api/v1/stores/{store}/payment-methods/BTC-LN", {"enabled": True, "config": {"connectionString": LN(node)}})
    if "httpError" in r: r = gf("PUT", f"/api/v1/stores/{store}/payment-methods/BTC-LN", {"enabled": True, "config": LN(node)})
    return "httpError" not in r, r

def setup():
    out = {}
    store = gf("GET", "/api/v1/stores")[0]["id"]; out["store"] = store
    ok, r = ensure_ln(store, "merchant"); out["merchant_ln"] = ok or r
    if not MARK.exists():
        gf("DELETE", f"/api/v1/stores/{store}/payment-methods/BTC-CHAIN")
        r = gf("POST", f"/api/v1/stores/{store}/payment-methods/BTC-CHAIN/wallet/generate",
               {"savePrivateKeys": True, "importKeysToRPC": False, "wordCount": 12, "scriptPubKeyType": "Segwit"})
        out["hotwallet"] = "httpError" not in r
        if out["hotwallet"]: MARK.write_text("ok\n")
    h.rpc("createwallet", "maxiaff") if "error" in (h.rpc("loadwallet", "maxiaff") or {}) else None
    mine = h.rpc("getnewaddress", wallet="maxiaff")
    bal = gf("GET", f"/api/v1/stores/{store}/payment-methods/BTC-CHAIN/wallet")
    if float(bal.get("confirmedBalance", 0)) < 0.5:
        addr = gf("GET", f"/api/v1/stores/{store}/payment-methods/BTC-CHAIN/wallet/address")["address"]
        h.rpc("sendtoaddress", addr, 1, wallet="maxiaff"); h.rpc("generatetoaddress", 2, mine)
    out["oc_processor"] = gf("PUT", f"/api/v1/stores/{store}/payout-processors/OnChainAutomatedPayoutSenderFactory/BTC-CHAIN",
                             {"intervalSeconds": 600, "processNewPayoutsInstantly": True, "feeBlockTarget": 1})
    out["ln_processor"] = gf("PUT", f"/api/v1/stores/{store}/payout-processors/LightningAutomatedPayoutSenderFactory/BTC-LN",
                             {"intervalSeconds": 600, "processNewPayoutsInstantly": True})
    stores = gf("GET", "/api/v1/stores")
    aff = next((s["id"] for s in stores if s["name"] == "Affiliate wallet (customer node)"), None) \
          or gf("POST", "/api/v1/stores", {"name": "Affiliate wallet (customer node)", "defaultCurrency": "BTC"})["id"]
    out["aff_store"] = aff
    ok, r = ensure_ln(aff, "customer"); out["customer_ln"] = ok or r
    out["customer_lnurl"] = gf("PUT", f"/api/v1/stores/{aff}/payment-methods/BTC-LNURL", {"enabled": True, "config": {"useBech32Scheme": True, "lud12Enabled": False}})
    out["lnaddr"] = gf("POST", f"/api/v1/stores/{aff}/lightning-addresses/affwallet", {"username": "affwallet", "currencyCode": "BTC"})
    out["lnurl"] = lnurl("http://127.0.0.1:14142/.well-known/lnurlp/affwallet")
    return out

if __name__ == "__main__":
    o = setup()
    for k, v in o.items(): print(k, "=", str(v)[:160])
