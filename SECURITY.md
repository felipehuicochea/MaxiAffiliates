# Security policy

## Supported versions
Only the latest release of MaxiAffiliates receives security fixes. It targets BTCPay Server 2.4.5 and later 2.4.x releases (see `docs/UPGRADE.md`).

## Reporting a vulnerability
Please **do not open a public issue** for security problems.

- Preferred: GitHub private vulnerability reporting ("Report a vulnerability" on the repository's Security tab).
- Email: **info@criptomonedastv.com** (subject "MaxiAffiliates security").
- Include the plugin and BTCPay versions, steps to reproduce, impact, and any proof of concept. Please don't send real funds, real customer data or production credentials.

What to expect:
- Acknowledgement within **3 business days**.
- An assessment and planned fix date within **10 business days**.
- A fixed release, then public disclosure (with credit, if you want it) **no later than 90 days** after your report, sooner once a fix ships. We will coordinate the date with you.

Please give us reasonable time to fix the problem before any public disclosure, and only test against your own BTCPay instance (regtest/testnet preferred). Good-faith research following this policy won't be pursued legally.

## Scope
In scope: the plugin's code (admin pages, public signup/referral/checkout endpoints, affiliate panel, payouts, emails, reports).
Out of scope: BTCPay Server core itself (report to the BTCPay Server project: https://github.com/btcpayserver/btcpayserver/security), your hosting/reverse proxy, and social-engineering.

The latest internal review is in `docs/SECURITY_REVIEW.md`.
