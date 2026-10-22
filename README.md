<img src="docs/assets/logo.png" alt="MaxiAffiliates logo" width="96">

# MaxiAffiliates for BTCPay Server

Affiliate program plugin for BTCPay Server (2.4.5+): referral links, commissions on paid invoices and subscription renewals, and payouts through BTCPay pull payments.

**Status: in development (milestones M1–M9 done: affiliates, attribution, commissions, payouts, affiliate panel, English + Spanish, security review, order confirmation callback). Release candidate 1.0.0, not published yet.**

En español: [README.es.md](README.es.md).

Developer setup: see [DEV.md](DEV.md). Design notes: [docs/DESIGN.md](docs/DESIGN.md). Website integration: [docs/INTEGRATION.md](docs/INTEGRATION.md).

User docs: [Admin guide](docs/ADMIN_GUIDE.md) · [Affiliate guide](docs/AFFILIATE_GUIDE.md) · [Upgrade notes](docs/UPGRADE.md) · [Changelog](CHANGELOG.md) · [Security policy](SECURITY.md)

Install: Server Settings → Plugins → Upload plugin → the `.btcpay` file from `./build-release.sh` (`dist/…`), then restart.

## Languages
English and Spanish (neutral Latin-American). The plugin adds its Spanish strings to BTCPay's "Spanish" dictionary (created if missing). Select it in Server Settings → Translations; install BTCPay's Spanish language pack there too to translate the rest of BTCPay.

## Support development
BTC tip address: `bc1q2pdqh45csx983dt0vedstn0sagm4m0zt8umpuy`

## License
MIT. See [LICENSE](LICENSE). Copyright (c) 2026 Felipe Huicochea.
