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
