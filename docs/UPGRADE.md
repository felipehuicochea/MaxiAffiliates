# Upgrade notes

## Upgrading the plugin
1. Upload the new `.btcpay` (or update it from the Plugin Directory), then restart BTCPay.
2. Database migrations for the plugin's own schema `BTCPayServer.Plugins.MaxiAffiliates` run automatically at startup. Check that `GET /plugins/maxiaffiliates/health` shows `"pendingMigrations": 0`.
3. Migrations are forward-only. Back up the BTCPay database before upgrading, and restore that backup if you need to roll back.
4. Uninstalling the plugin leaves its schema in place. Drop it by hand if you really want the data gone.

## Upgrading BTCPay Server (what to check)
The plugin is built and tested against **BTCPay Server 2.4.5** (`Dependencies: BTCPayServer >= 2.4.5`). It uses a set of BTCPay *internal* APIs that are not part of the Greenfield API and can change in any BTCPay release. Before upgrading BTCPay in production, run the plugin test suite and `dev/fresh_install.py` against the new version on regtest, and check the items below.

| BTCPay internal used | What for | What to check after a BTCPay upgrade |
|---|---|---|
| `BaseBTCPayServerPlugin`, `AddUIExtension`, `AddPolicyDefinitions`, `AddReportProvider`, `AddTranslationProvider` | registration, nav entry, permissions, Reporting, default strings | plugin loads, Affiliates menu shows, permissions listed |
| `BasePluginDbContext` / `[PluginDatabase]` / `AddPluginDbContext` | own schema + migrations | `/health` 0 pending |
| `EventAggregator` + `EventHostedServiceBase`; `InvoiceEvent` names (`invoice_paymentSettled`, `invoice_confirmed`, `invoice_completed`, `invoice_markedComplete`, `invoice_markedInvalid`, `invoice_refund`) | crediting and reversing commissions | M4/M5 integration tests; settle and refund an invoice on regtest |
| `SubscriptionEvent` (credit refunded), `PlanCheckoutData`, the subscriber `SUBS#{checkoutId}` invoice tag, `PlanCheckouts` / subscriber invoices tables | subscription renewals and attribution | M4 renewal test; plan checkout with `invoiceMetadata.maxiaffRef` |
| `InvoiceRepository`, `StoreRepository`, `ApplicationDbContextFactory` | reading invoices, stores, refund pull payments | M5 refund tests |
| `RateFetcher`, `DefaultRulesCollection`, store rate rules | BTC rate at settlement | base-currency override test |
| `PullPaymentHostedService.Claim` / `ClaimRequest`, `PayoutMethodId`, `PayoutMethodHandlerDictionary`, `PayoutEvent` | creating and following payouts | M6 acceptance (LN + on-chain) |
| `EmailSenderFactory` (`BTCPayServer.Plugins.Emails`) | magic links and notices | M7 acceptance with Mailpit |
| `LocalizerService` and tables `lang_dictionaries` / `lang_translations` | seeding the Spanish dictionary | `dev/screens_m8.py` |
| `ReportProvider` / `QueryContext` / `ViewDefinition` | Reporting views | Reporting → Affiliate commissions |
| `NicolasDorier.RateLimits` zones `btcpayregister`, `btcpaylogin`, `publicinvoices` | public rate limits | `EndpointPostureTests` |
| `CurrencyNameTable`, `DisplayFormatter`, `BTCPayNetworkProvider` | formatting, address validation | admin pages render |
| `_LayoutSimple`, BTCPay tag helpers (`permission`, `text-translate`) | views | pages render; strings translated |

If a BTCPay release breaks one of these, publish a plugin update with a tighter `Dependencies` condition. For example, keep `>=2.4.5 <2.5.0` until 2.5 is verified.

## Version history
See `CHANGELOG.md`.
