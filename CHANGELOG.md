# Changelog

All notable changes to MaxiAffiliates. The format follows [Keep a Changelog](https://keepachangelog.com/) and versions follow SemVer.

## [1.0.0] - unreleased
First release candidate for BTCPay Server 2.4.5+.

### Added
- **Affiliates:** public signup, admin approve, suspend, reactivate and ban; Lightning-address/LNURL or on-chain payout destination; two permissions (view, manage).
- **Referrals:**
  - `/aff/{store}/{code}` links with a 30-day first-click cookie;
  - a website helper script;
  - a referral-carrying plan checkout endpoint;
  - a metadata contract (`maxiaffRef`) for backends.
- **Commissions:**
  - created on settled invoices and subscription renewals;
  - percentage or flat rules per plan;
  - scope of first payment, lifetime or first N payments;
  - base-currency override;
  - retry when an exchange rate is missing.
- **Refund handling:**
  - hold period before commissions become payable;
  - reversal on refund or invalidation, proportional for partial refunds;
  - clawback netted against the next payout after a commission was paid;
  - minimum payout with carry-over.
- **Payouts:** weekly or monthly cycle; a "commissions due" report with CSV; manual approval (the default) or automatic; payouts through BTCPay store payouts and processors (Lightning and on-chain); payout state tracking.
- **Affiliate panel:** passwordless sign-in by email magic link, or an admin-issued link; stats, balance, commissions, payout history; payout address change with notice email.
- **Reports:** "Affiliate commissions" and "Affiliate payouts" in BTCPay Reporting.
- **Languages:** English and Spanish (neutral Latin-American); Spanish strings are seeded into BTCPay's "Spanish" dictionary.
- **Order confirmation callback:** signed (HMAC-SHA256, timestamped) POST to a per-store URL on every settled invoice or subscription payment, with plan/offering/subscriber ids, amount, affiliate code and commission outcome; idempotent event id; retries with exponential backoff (8 attempts, capped at 1 h); delivery log with redelivery; test send; https-only/public-host guard in Release builds.
- **Docs:** admin guide, affiliate guide (EN/ES), integration guide (EN/ES), upgrade notes, security policy.

### Security (M9 review, see `docs/SECURITY_REVIEW.md`)
- The post-payment redirect is restricted to the store's landing host or the BTCPay host.
- Per-IP rate limits on signup, panel sign-in, link redemption and checkout creation.
- Strict email validation; spreadsheet-formula escaping in Reporting exports and the cycle CSV.
- The health endpoint no longer lists migration names.
- Tests pin the authz, CSRF and rate-limit posture of every endpoint, plus store isolation and payout race tests.
