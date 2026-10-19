# MaxiAffiliates security review (M9)

Date: 2026-10-19 · Plugin 1.0.0 on BTCPay Server 2.4.5 · Reviewer: internal (development team) · Scope: all plugin code in `src/`.

Severity scale: **High** (direct loss of funds or account takeover), **Medium** (abuse with real impact under plausible conditions), **Low** (limited impact or needs unusual conditions), **Info** (hardening / no direct impact).

## Summary

| # | Area | Finding | Severity | Status |
|---|---|---|---|---|
| F1 | Open redirect | `successRedirectUrl` on the public checkout accepted any http(s) URL, so a link on the store's BTCPay domain could send paying buyers to a third-party site | Medium | Fixed |
| F2 | Rate limits | Public signup, panel sign-in request, link redemption and checkout creation had no per-IP limit (signup spam, sign-in email flooding across many affiliates, checkout row spam) | Medium | Fixed |
| F3 | Input validation / CSV injection | Emails were only checked for an `@`; values such as `=HYPERLINK(...)@x.io` or `Ana <a@x.io>` were accepted and later appeared in BTCPay Reporting CSV exports unescaped | Medium | Fixed |
| F4 | Info disclosure | Anonymous `/plugins/maxiaffiliates/health` listed every migration name | Low | Fixed |
| F5 | Payout address change | A stolen panel session could change the payout address; mitigated (notice email, other sessions revoked, manual approval default) but no admin-side warning | Low | Accepted (recommendations below) |
| F6 | Crash window | If BTCPay stops between claiming a batch (Approved) and storing the payout id, the batch stays "Payout created" without an id. No double payment is possible; needs manual check | Low | Accepted, documented |
| F7 | Link-click counter | Anyone can inflate an affiliate's "link clicks" stat | Info | Accepted (display-only) |
| F8 | Dependencies | `dotnet list package --vulnerable` reports transitive advisories in packages that BTCPay 2.4.5 itself provides (System.Security.Cryptography.Pkcs 10.0.0, Microsoft.AspNetCore.SignalR.Common 10.0.12) | Info | Host-provided; plugin ships none of them |

No High findings. Everything else in scope was reviewed and found sound (details below).

## Findings and fixes

### F1 Open redirect after payment (Medium, fixed)
`GET/POST /plugins/maxiaffiliates/{store}/checkout` creates a BTCPay plan checkout and passed `successRedirectUrl` through if it was any absolute http(s) URL. An attacker could build a link on the store's trusted BTCPay domain that, after a real payment, redirects to a phishing page.
**Fix:** `InputSafety.SafeSuccessRedirect` keeps the URL only when its host equals the store's landing-page host or the BTCPay host, rejects user-info (`https://good.com@evil`), non-http schemes and look-alike hosts. Otherwise BTCPay shows its own receipt.
**Tests:** `InputSafetyTests.SuccessRedirect` (9 cases). The referral link `/aff/{store}/{code}` only redirects to the admin-configured landing URL (unchanged, not attacker-controlled).

### F2 Missing rate limits (Medium, fixed)
**Fix:** BTCPay's own per-IP zones (`NicolasDorier.RateLimits`) on the anonymous write endpoints:
- signup POST → `btcpayregister` (production: 2/min, burst 2)
- panel "email me a link" POST and link redemption POST → `btcpaylogin` (5/min, burst 3)
- checkout GET/POST → `publicinvoices` (4/min, burst 10, extra requests delayed)

These come on top of the existing per-affiliate cap of 3 magic links per 15 minutes. (BTCPay relaxes these zones in its Development environment.)
**Evidence:** `EndpointPostureTests.PublicWritesAreRateLimited`. On the fresh production-mode install, 13 rapid checkout calls: the first 3 were immediate, later ones were delayed ~15 s each.

### F3 Weak email validation → spreadsheet formula injection (Medium, fixed)
**Fix:** `InputSafety.NormalizeEmail` accepts only a plain addr-spec (`local@domain.tld`, lower-cased, ≤254 chars). It rejects display names, quotes, spaces, `<>`, formula-looking local parts and single-label domains. It is used by signup, panel sign-in and the checkout email.
Defence in depth: `InputSafety.SpreadsheetSafe` (leading `= + - @ TAB CR` gets a `'` prefix, numbers untouched) now applies to text columns of both BTCPay Reporting providers and to the M6 cycle CSV.
**Tests:** `InputSafetyTests.Email` and `.Spreadsheet`, `SecurityIntegrationTests.SignupRejectsFormulaAndDisplayNameEmails`, plus the existing `CycleScheduleTests.CsvEscapesAndBlocksFormulas`.

### F4 Health endpoint detail (Low, fixed)
It now returns plugin id, version, schema, and *counts* of applied and pending migrations only.

### F5 Payout address change by a session thief (Low, accepted)
Existing controls:
- passwordless links (single-use 15-minute magic links, or 30-day admin links that can be revoked);
- sessions are HttpOnly, SameSite=Lax cookies scoped to the panel path, and expire after 7 days;
- saving an address revokes all other sessions and links, and emails a change notice when email is configured;
- destinations are validated for the network;
- payouts need manual approval by default, and the cycle report shows each destination.

**Recommendation for admins:** keep manual approval and glance at destinations before approving. *Future:* show a "destination changed recently" badge in the cycle report and allow an optional cooldown.

### F6 Crash between claim and payout id (Low, accepted)
Approval first atomically flips Draft→Approved (`UPDATE … WHERE State = 'Draft'`), then calls BTCPay. A crash in between leaves an Approved batch without a payout id; it never creates a second payout. The BTCPay payout carries `metadata.batchId` so it can be matched by hand. Documented in the admin guide (troubleshooting).

### F7 Click counter inflation (Info, accepted)
The counter is display-only and never affects money. Attribution is unaffected: first-click cookie plus settlement-time checks.

### F8 Dependency audit (Info)
`dotnet list package --vulnerable --include-transitive` (2026-10-19): the only advisories are transitive packages of BTCPay Server 2.4.5 itself: System.Security.Cryptography.Pkcs 10.0.0 (GHSA-846f-f764-76mx High, GHSA-2fmv-rv2c-59x7 Moderate) and Microsoft.AspNetCore.SignalR.Common 10.0.12 (GHSA-334f-vvp4-7g6p High).
The `.btcpay` package contains only the plugin DLL plus the translation JSON files. All other assemblies are loaded from the host. Fix: run a BTCPay release that bumps them; the plugin needs no change. Plugin-only packages (Roslyn, EF Design, Npgsql EF) are referenced in Debug only and are not shipped.

## Areas reviewed without new findings

**Authorization and store isolation.** Every admin action is under `/stores/{storeId}/plugins/maxiaffiliates/...` with the plugin policy `CanViewAffiliates`. Every mutating POST additionally needs `CanManageAffiliates`. BTCPay resolves the store from the route and checks the user's role permission.
Every service query filters by that `storeId` (batches, cycles, commissions, rules, affiliates, panel links). An id from another store returns 404 or "not found".
*Tests:* `EndpointPostureTests` pins the posture of all 31 actions, so a new unprotected action fails CI. `SecurityIntegrationTests.OtherStoreCannotTouchThisStoresObjects` covers approve, cancel, cycle report, reverse, rule delete, panel link and ban across stores.
The affiliate panel only ever loads the signed-in affiliate's rows (`AffiliatePanelIntegrationTests`).

**CSRF.** All 17 POST actions except one carry `[ValidateAntiForgeryToken]`; the exception is the public JSON checkout. That endpoint is intentionally CORS-callable without cookies: it only creates a checkout, and it is rate-limited. Pinned by a test.
Panel cookies are SameSite=Lax, and magic links need a POST from the confirm page.

**Open redirects (others).** Login and logout redirects are fixed action routes. The referral landing URL is admin-set (http/https only). The `/aff` code goes through a strict regex.

**Tokens and sessions.** 256-bit `RandomNumberGenerator` tokens, base64url encoded. Only the SHA-256 is stored, and lookup is by hash (no timing oracle on the secret).
- Magic links are consumed atomically (`UPDATE … WHERE UsedAt IS NULL`).
- Expiry is enforced server-side.
- Logout revokes the session in the database; banning revokes every token.
- Pages are `Cache-Control: no-store` and `Referrer-Policy: no-referrer`.
- The admin link is shown once, through encrypted TempData.

**XSS.** All views use Razor encoding. There is no `Html.Raw` (verified with grep).
- Email bodies HTML-encode the store name, the URL and the destination.
- The website helper script builds no HTML from input.
- User-controlled fields (name ≤100 chars, email, destination) are always encoded.

**SSRF.** The plugin makes no outbound HTTP calls. LNURL / Lightning address resolution is done by BTCPay's own payout handler with BTCPay's SSRF protection.
The regtest-only `BTCPAY_SSRFEXCEPTIONS=127.0.0.1:14142` exists only in `dev/run-btcpay.sh`, which is not part of the package. `ReleaseHygieneTests` fails if `SSRF`, `127.0.0.1` or `localhost:` ever appears in plugin sources, or if `HttpClient` / `WebRequest` is used.
The fresh-install test runs BTCPay without any SSRF exception.

**Payout double-spend and races.**
- Batch creation claims commissions with `UPDATE … SET PayoutBatchId = @b WHERE PayoutBatchId IS NULL`. Postgres row locks make a concurrent claim lose.
- Approval claims the state transition before calling BTCPay.
- Cancel only works on Draft.
- Refunds after payout become clawbacks netted against the next payout (M5).
- *Tests:* `ConcurrentCycleRunsNeverBatchACommissionTwice` (6 parallel cycle runs → every commission in exactly one batch) and `ConcurrentApprovalsCreateExactlyOnePayout` (8 parallel approvals → one gateway call).

**Secrets in logs.** Log lines carry ids, counts and states only. No tokens, links, emails, addresses or API keys (grep-reviewed). The magic-link URL is never logged.
The dev tooling keeps credentials in gitignored files and never prints them.

## Residual recommendations
1. Run BTCPay behind HTTPS (cookies are then `Secure`).
2. Configure store SMTP, so that magic links and address-change notices work.
3. Keep manual payout approval unless volumes require auto-approval.
4. Update BTCPay Server promptly (F8).
