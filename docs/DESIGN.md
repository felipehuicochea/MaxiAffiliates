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
