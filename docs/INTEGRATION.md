# MaxiAffiliates: website integration (M3)

All examples use `https://BTCPAY` (your BTCPay Server) and `STORE_ID`. En español: [INTEGRATION.es.md](INTEGRATION.es.md).

## 1. Referral links
`https://BTCPAY/aff/STORE_ID/CODE`
- **Active code:**
  - Sets a first-party, HttpOnly cookie `maxiaff_ref_STORE_ID` on the BTCPay host, lasting 30 days with first click winning.
  - Redirects to the store's **Landing page URL** (set in Store → Affiliates) with `?ref=CODE`.
  - If the visitor already has a valid earlier click, the earlier code is kept and passed on.
- **Unknown, pending, suspended or banned code:** redirects to the landing page without a ref and sets no cookie.
- **No landing URL configured:** 404.
- You can also share any page of your own site as `https://your.site/page?ref=CODE`. The helper below picks that up.

## 2. Website helper (no backend needed)
```html
<script src="https://BTCPAY/plugins/maxiaffiliates/js/maxiaff.js" data-store="STORE_ID"></script>
<button data-maxiaff-offering="OFFERING_ID" data-maxiaff-plan="PLAN_ID">Subscribe</button>
```
- **Capturing the ref:** on any page with `?ref=CODE`, the helper stores the code in `localStorage` plus a `maxiaff_ref` cookie on your site, for 30 days. An earlier code is never overwritten (first click).
- **Checkout buttons:** clicking an element with `data-maxiaff-plan` creates a BTCPay plan checkout carrying the ref and redirects the buyer to it. Optional attributes: `data-maxiaff-email`, `data-maxiaff-success`.
- **JS API:**
  - `MaxiAffiliates.getRef()`
  - `MaxiAffiliates.checkout({offeringId, planId, email, successRedirectUrl, redirect})`, which returns a promise of `{checkoutId, url, attributed}`.

## 3. Checkout endpoint (helper, plain links or your backend)
- **JSON:** `POST https://BTCPAY/plugins/maxiaffiliates/STORE_ID/checkout`, body `{"offeringId","planId","ref","email","successRedirectUrl"}`, any content type.
  - Returns `{checkoutId, url, attributed}`.
  - Unknown plan gives 404; missing ids give 400.
- **No JS:** `GET https://BTCPAY/plugins/maxiaffiliates/STORE_ID/checkout?offeringId=…&planId=…&ref=…` returns a 302 to the checkout. The BTCPay cookie is also used here.
- **How the code is chosen:** the BTCPay cookie and the explicit `ref` are combined with first-click-wins (30 days). The code is dropped if the affiliate isn't Active, or if the buyer email equals the affiliate's email (self-referral).
- **CORS:** BTCPay applies its global AllowAnyOrigin policy (without credentials), so cross-site calls carry the ref in the body.

- **successRedirectUrl** (security, M9): only accepted when its host is the store's landing-page host or the BTCPay host (http/https, no user-info). Other values are dropped and BTCPay shows its own receipt.
- **Rate limits:** the checkout endpoint uses BTCPay's `publicinvoices` zone per IP (in production about 4/min, burst 10; extra requests are delayed).

## 4. Metadata contract (for backends calling Greenfield directly)
If your backend creates checkouts itself (Greenfield `POST /api/v1/plan-checkout`, or `POST /api/v1/stores/{id}/invoices` for one-time sales), include the referral code:
```json
"invoiceMetadata":       { "maxiaffRef": "CODE" },
"newSubscriberMetadata": { "maxiaffRef": "CODE" }
```
(For plain invoices use `metadata.maxiaffRef`.) The plugin endpoint also adds `maxiaffAffiliateId`. Attribution happens when the invoice settles (M4), which re-checks the affiliate status and self-referral against the buyer email.

## 5. Order confirmation callback (seller site)

In the plugin admin, **Order callback** tab: generate a signing secret (copy it, it is shown once), enter your callback URL (https, public host), enable it and save. **Send test event** sends a `"type":"test"` event. Every settled invoice or subscription payment of the store then produces one event, whether or not an affiliate was involved. Payload fields: see `docs/DESIGN.md` (v1 addition).

Headers: `MaxiAff-Signature: t=<unix seconds>,v1=<hex>`, `MaxiAff-Event-Id`, `MaxiAff-Delivery-Id`.

**Verify every request** before trusting it:
1. Parse `t` and `v1` from `MaxiAff-Signature`.
2. Reject if `|now - t| > 300` seconds (replay protection).
3. Compute `HMAC-SHA256(secret, "<t>." + raw request body)` as lowercase hex and compare with `v1` in constant time. Use the raw bytes, not re-serialized JSON.
4. Deduplicate on `eventId`: retries and manual redeliveries reuse it.
5. Answer `2xx` quickly. Network errors, timeouts (15 s), `408`, `425`, `429` and `5xx` are retried after 10 s, 30 s, 90 s, 4.5 min, 13.5 min, 40 min and 1 h (8 attempts in total); other `4xx` answers are final. Redirects are not followed.

```php
$raw = file_get_contents('php://input');
parse_str(str_replace(',', '&', $_SERVER['HTTP_MAXIAFF_SIGNATURE'] ?? ''), $sig);
$ok = isset($sig['t'], $sig['v1']) && abs(time() - (int)$sig['t']) <= 300
   && hash_equals(hash_hmac('sha256', $sig['t'] . '.' . $raw, getenv('MAXIAFF_CALLBACK_SECRET')), $sig['v1']);
if (!$ok) { http_response_code(401); exit; }
$event = json_decode($raw, true);   // then: if (already_processed($event['eventId'])) { http_response_code(200); exit; }
```

```python
import hmac, hashlib, time
def verify(secret: str, header: str, raw: bytes) -> bool:
    parts = dict(p.strip().split("=", 1) for p in header.split(",") if "=" in p)
    t = int(parts.get("t", "0"))
    mac = hmac.new(secret.encode(), f"{t}.".encode() + raw, hashlib.sha256).hexdigest()
    return abs(time.time() - t) <= 300 and hmac.compare_digest(mac, parts.get("v1", ""))
```

Only need "invoice settled" without affiliate fields? BTCPay's own store webhooks (Store → Webhooks) also work; the plugin callback adds plan/offering/subscriber resolution, the affiliate code and the commission outcome, and a timestamped signature.
