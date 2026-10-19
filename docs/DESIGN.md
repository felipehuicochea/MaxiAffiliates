# MaxiAffiliates: design notes (M1)

## Data layer
- EF Core context `MaxiAffiliatesDbContext : BasePluginDbContext<>`, registered with `AddPluginDbContext` (BTCPay 2.4.5's recommended plugin API; migrations run at startup).
- Schema: `BTCPayServer.Plugins.MaxiAffiliates`. Migration history table: `MaxiAffiliates_Migrations`. Enums are stored as strings.
- Tables:
  - `Affiliates`: unique (StoreId, Code) and (StoreId, Email)
  - `CommissionRules`: per plan, or null for the store default; percent or flat; FirstPayment, Lifetime or FirstN
  - `Referrals`: the customer→affiliate binding; unique (StoreId, CustomerKey); tracks credited-payment count
  - `Commissions`: unique (StoreId, InvoiceId) enforces one commission per invoice; base amount and currency, sats at the settlement rate, Pending/Payable/Paid/Reversed, PayableAt (hold period), FK to payout batch
  - `PayoutBatches`: per affiliate per cycle; pull payment and payout ids
  - `StoreSettings`: per-store defaults: 20 % lifetime, 30-day hold, LN and on-chain, manual approval, min payout (sats, carry-over), weekly or monthly cycle and day, base-currency override (null = store default)

## Attribution (v1)
- Configurable in v1: **scope rules only** (first payment / lifetime / first N), on `CommissionRule` and the store default.
- Click model is **hard-coded to first-click with a 30-day cookie** (`maxiaff_ref`). It lives behind `IAttributionStrategy` (`FirstClickAttributionStrategy`, `AttributionDefaults.CookieLifetime`).
- A later first-vs-last-click store setting only needs a new strategy implementation plus a setting field and a migration. Callers already depend on the interface.
- Once a customer is bound, the binding is permanent per store (DB unique index). Renewals are credited to that affiliate.

## M2: affiliates and admin
- **Public signup:** `GET/POST /plugins/maxiaffiliates/{storeId}/signup`, anonymous with antiforgery.
  - Email is normalized to lower case and must be unique per store.
  - Payout method must be enabled in the store settings.
  - Payout destination: a Lightning address (`name@domain`) or LNURL, or an on-chain address checked against the server's network (NBitcoin).
  - Each affiliate gets an 8-character code from an unambiguous alphabet.
  - New affiliates start as Pending, or Active if the store's `AutoApproveAffiliates` is on.
- **Admin list:** `GET /stores/{storeId}/plugins/maxiaffiliates/affiliates` with status filter and search. Store nav item "Affiliates" in `store-integrations-nav`.
- **Actions:** `POST .../affiliates/{id}/{op}` where op is Approve, Suspend, Reactivate or Ban. Allowed transitions:
  - Pending → Active
  - Active ⇄ Suspended
  - any → Banned (terminal, confirm dialog)
  - Actions are store-scoped.
- **Permissions:** `btcpay.store.canviewaffiliates` (included by CanViewStoreSettings) and `btcpay.store.canmanageaffiliates` (included by CanModifyStoreSettings), registered as PolicyDefinitions so they show up in API-key scopes.
- **Referral links:** only Active affiliates resolve (`AffiliateService.FindActiveByCode`), so Pending, Suspended and Banned links are ignored.
- **Localization:**
  - Every UI string goes through BTCPay's localizer (`text-translate="true"` / `StringLocalizer[...]`).
  - The keys live in embedded `Resources/Translations/en.json` and are registered via `IDefaultTranslationProvider`, so they appear in Server Settings → Translations.
  - A unit test fails if any view or controller string is missing from en.json. M8 adds es.json.

## M3: referral capture
- **Click endpoint:** `/aff/{storeId}/{code}`. First-click cookie `maxiaff_ref_{storeId}` = `CODE.unixSeconds`, HttpOnly, SameSite=Lax, Max-Age 30 days. Redirects only to the store's configured `LandingUrl` (new column, migration `AddLandingUrl`), so there's no open redirect.
- **Attribution logic:** all decisions go through `IAttributionStrategy` (`FirstClickAttributionStrategy`) via `ReferralService` (`PickCode` / `CookieAfterClick` / `Resolve`). Resolve applies:
  - Active-only affiliates
  - per-store codes
  - the self-referral check against the buyer email
- **Checkout:** `PlanCheckoutService` creates the core `PlanCheckoutData` with `invoiceMetadata` and `newSubscriberMetadata` set to `{maxiaffRef, maxiaffAffiliateId}`. This mirrors Greenfield `POST /api/v1/plan-checkout` in 2.4.5. It **depends on core data types**, so it must be re-verified on every BTCPay upgrade.
- **Website helper:** `/plugins/maxiaffiliates/js/maxiaff.js`, an embedded resource. It mirrors first-click and 30 days in localStorage plus a site cookie.
- **CORS:** BTCPay's global CORS middleware (AllowAnyOrigin, no credentials) answers preflights before plugin actions run, so per-store origin restriction isn't possible. The helper posts `text/plain` without credentials, with the ref in the body. The endpoint only creates checkout sessions, the same exposure as public plan-checkout links.
- See docs/INTEGRATION.md for the public contract.

## M4: commissions, rules, base currency

- **Trigger.** `InvoiceSettledListener` (an `EventHostedServiceBase`) subscribes to `InvoiceEvent` and acts when `Invoice.Status == Settled` (it fires on paymentSettled, confirmed, completed and markedComplete). Crediting is idempotent: there's one commission per (store, invoice), enforced by a unique index, and replayed events return `AlreadyCredited`.
- **Mapping.** InvoiceEntity becomes `SettledInvoice`:
  - ref = metadata `maxiaffRef`
  - customer = `buyerEmail`
  - plan = metadata `planId`, else the `SUBS#{checkoutId}` tag's plan checkout `PlanId`, else `itemCode`
  - subscriber = the checkout's `SubscriberId`
  - store default currency = `StoreBlob.DefaultCurrency`
  - These are BTCPay core internals; re-verify them on upgrades.
- **Renewals.** Subscriptions renewal invoices don't carry the ref, but they do carry the subscriber's email as `buyerEmail`. The `Referral` binding (customer key `email:<lower>`) created on the first credited payment therefore carries renewals. It is first click: an existing binding always beats a new ref. Suspended or banned affiliates keep their bindings but earn nothing while inactive. Self-referrals are rejected.
- **Rule resolution.** Enabled plan rule → enabled store-wide rule (PlanId null) → program settings (`DefaultPercent`, `DefaultScope`, `DefaultFirstN`). There's one rule per plan (upsert).
- **Scope.** `FirstPayment` covers payment #1 only. `FirstN` covers payments #1..N. `Lifetime` covers every payment. "Credited so far" is `Referral.CreditedPayments`, so out-of-scope payments don't count.
- **Amounts.** Percent is clamped to 0–100. Flat is in the base currency and capped at the sale amount. Sats are computed at the settlement rate and rounded toward zero. `Status = Pending`, `PayableAt = settledAt + HoldDays`.
- **Base currency.** It's `BaseCurrencyOverride`, or the store default currency when blank. Conversion uses the rates stored on the invoice (`TryGetRate(BTC_x)`), with a fallback to `RateFetcher` using the store's rate rules. BTC = 1 and SATS = 1e8 per BTC.
  - If no rate is available, the payment is **not credited** (logged warning, `RateUnavailable`). The store's rate source must therefore provide the override currency. For example, the default Kraken script has no MXN.
- **Admin.** There are three tabs: Affiliates, Commissions and Settings.
  - `/stores/{id}/plugins/maxiaffiliates/settings` holds the program settings and the per-plan rules (plan picker built from the store's Subscriptions plans).
  - `/stores/{id}/plugins/maxiaffiliates/commissions` is the ledger.
  - Manage actions need `btcpay.store.canmanageaffiliates`.

## M5: hold, clawback, threshold

- **Hold period.** A commission is created as `Pending` with `PayableAt = settledAt + HoldDays`. `CommissionMaintenanceService` runs every 10 minutes (the first run is 30 s after start). It moves `Pending` to `Payable` once `PayableAt <= now`. Admins can trigger it from **Commissions → Process holds and retries now**.
- **Reversals.** All of these are handled by `InvoiceSettledListener` and applied in `CommissionLedger`, idempotently via `CommissionAdjustments.SourceRef`:
  - `invoice_refund`: a refund pull payment was created. It's sized from the pull payment's `Limit` and `Currency` when that currency is the sale currency, the base currency, BTC or SATS; otherwise it counts as a full refund. The clawback happens when the refund is created, not when it is claimed, which is the conservative choice.
  - `invoice_markedInvalid`: full reversal. BTCPay 2.4.5 doesn't allow Settled → Invalid through the API, so this handler is defensive and covered by tests only.
  - Subscriptions `CreditRefunded` hits the subscriber's latest commission, sized against its sale amount.
  - The manual **Reverse** button on a ledger row.
- **Clawback rules.**
  - A full refund of an unpaid commission (Pending or Payable) marks it `Reversed`. It never matures, and any earlier partial clawbacks on it stop counting.
  - A partial refund, or any refund after the commission was `Paid`, adds a negative `CommissionAdjustment`, capped at what's left of that commission.
  - An adjustment counts toward the balance once its commission is `Payable` or `Paid`.
- **Threshold.** For each affiliate, net = unbatched Payable sats + effective adjustments.
  - `Due` when net ≥ `MinPayoutSats` (at least 1 sat).
  - Otherwise it carries over: `BelowThreshold`, or `NegativeCarryOver` when net is negative.
  - Non-active affiliates are `AffiliateInactive` (held).
  - M6 builds payout batches from `Due` balances and sets `PayoutBatchId` on the commissions and adjustments it pays.
- **Missing rates.** A `RateUnavailable` result goes into `PendingCredits`.
  - Backoff is 5 min doubling, capped at 6 h. Several settle events for one invoice count as one attempt.
  - It gives up after 7 days.
  - "Process now" retries every waiting credit immediately, including ones it gave up on.
  - Retries rebuild the invoice from `InvoiceRepository` and use the store's current rate rules.
- **Display.** Fiat amounts are rounded to the currency's divisibility (`CurrencyNameTable`). Scope, type, balance-state and reversal-reason labels are friendly localizable keys.

## M6: payout cycle and payouts

- **Cycle.** `PayoutCycleService.CurrentCycle` computes the latest boundary at UTC midnight:
  - Weekly: `PayoutDay` 0 (Sunday) to 6.
  - Monthly: day 1 to 28.
  - The maintenance service, every 10 minutes, runs a store's cycle once per boundary (unique `(StoreId, CycleKey)`).
  - A program configured after the current boundary starts at the next one.
  - Admins can also **Run payout cycle now** (a manual cycle with its own key).
- **Report.** A cycle matures holds that have ended, takes the `Due` balances (payable commissions plus clawbacks ≥ minimum), and creates one `PayoutBatch` per affiliate. It claims the items by setting `PayoutBatchId` on the unbatched rows only.
  - Below-minimum balances carry over and are counted on the cycle.
  - A store-disabled payout method or a non-active affiliate keeps the balance.
  - **Commissions due report**: `/stores/{id}/plugins/maxiaffiliates/payouts/{cycleId}`.
  - **CSV**: `/payouts/{cycleId}/csv` (UTF-8 BOM, RFC 4180, formula-injection guard on text cells).
- **Approval.** Manual is the default: batches start "Awaiting approval", and you approve them one at a time or all together. With `AutoApprovePayouts`, approval happens right after the cycle runs.
  - Approval moves the batch Draft → Approved atomically, so there's no double payout, and creates a **store payout**. It goes through `PullPaymentHostedService.Claim` with no pull payment and is pre-approved, the same path as Greenfield `POST /stores/{id}/payouts`.
  - Method: `BTC-LN` or `BTC-CHAIN`, to the destination snapshotted from the affiliate. The store's automated payout processors (or a manual send) pay it.
  - If destination validation or the claim fails, the batch is `Failed` with the error, and its items go back to the balance.
- **Tracking.** `PayoutEvent`, plus reconciliation on each maintenance run (events can race storing the payout id):
  - InProgress → `Sent`.
  - Completed → `Completed`, and its commissions become `Paid` with `PaidAt`.
  - Cancelled → `Cancelled`, and its items are released.
  - A refund after payout becomes a clawback (M5) netted in the next cycle.
- **Abstraction.** `IPayoutGateway`, with `BtcPayPayoutGateway` in production; tests use a fake.

### Lightning dev setup
`docker compose --profile ln up -d` starts two CLN nodes (`btcpayserver/lightning:v26.06.1`, 256 MB limit each).

- RPC sockets are in `dev/.ln/*`.
- The entrypoint binds P2P on 0.0.0.0:39735/39736 (regtest only).
- `dev/ln_setup.py` opens a 2M-sat channel merchant → customer.
- `dev/m6_setup.py` sets up:
  - the store's LN node (merchant), a hot on-chain wallet with funds, and the automated payout processors (instant)
  - an "Affiliate wallet" store on the customer node with a Lightning address. Its LNURL (http://127.0.0.1) is the LN affiliate's destination.
- BTCPay's SSRF protection blocks local LNURLs, so `dev/run-btcpay.sh` sets `BTCPAY_SSRFEXCEPTIONS=127.0.0.1:14142` for dev only.

## M7: Affiliate panel and admin reports

**Access (why magic link + token link).** Affiliates are not BTCPay users, so the panel has its own passwordless sign-in at
`/plugins/maxiaffiliates/{storeId}/panel`. BTCPay exposes the store's (or, if allowed, the server's) SMTP through
`EmailSenderFactory`, so when `IsComplete(storeId)` is true the login page sends a **magic link by email** (single use,
15 minutes, max 3 per 15 minutes per affiliate, identical "check your email" response for unknown addresses). Many stores
never set up SMTP, so the admin can also issue a **secure token link** ("Panel link" on the Affiliates list): shown once,
valid 30 days, reusable until reissued (reissue revokes the previous one). Both links open a confirm page and only a POST
redeems them, so mail scanners that prefetch links cannot burn them.

Tokens are 256-bit `RandomNumberGenerator` values (base64url); only SHA-256 hashes are stored (`AccessTokens` table, kinds
MagicLink / AdminLink / Session). A redeemed link creates a 7-day session in an HttpOnly, SameSite=Lax cookie (Secure on
HTTPS) scoped to the panel path of that store. Pending and suspended affiliates can sign in (read-only for suspended);
banned cannot, and banning revokes all their tokens. Changing the payout address ends every other session and link and
emails the affiliate a change notice when email is configured. Pages are `Cache-Control: no-store`; auth pages send
`Referrer-Policy: no-referrer`.

**Panel.** Referral link + code, stats (link clicks, referred customers, sales, earned net of clawbacks, paid out), balance
(pending/on hold with next release date, payable, clawbacks, next payout and its state vs. the minimum), last 50
commissions, payout history (cancelled batches hidden), and payout method/address update (validated for the store's
network and enabled methods; already approved payouts keep their snapshot address). Link clicks are counted on
`/aff/{store}/{code}` for active codes (`Affiliates.LinkClicks`).

**Admin reports.** Two `ReportProvider`s appear in BTCPay's Reporting page (and `POST /api/v1/stores/{id}/reports`), with
BTCPay's built-in filters, charts and CSV export: *Affiliate commissions* (per commission: affiliate, invoice, plan, sale,
commission, sats, clawback, status, payable/paid dates, payout id) and *Affiliate payouts* (per batch).
