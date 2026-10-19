# MaxiAffiliates admin guide

For store owners and staff running an affiliate program on BTCPay Server 2.4.5+. (Español: la interfaz está traducida; ver «Idioma» abajo.)

## 1. Install
1. Server Settings → Plugins → **Upload plugin**, choose `BTCPayServer.Plugins.MaxiAffiliates.btcpay`, then **Restart now**. Once it is listed in the Plugin Directory you can install it from there instead.
2. After the restart, every store gets an **Affiliates** entry in its menu.
3. Recommended:
   - HTTPS in front of BTCPay.
   - Store email (Store → Settings → Emails) so affiliates can sign in by email.
   - A hot wallet or Lightning node with BTCPay's automated payout processors if you want payouts sent automatically.

## 2. Permissions
Two permissions can be granted per store role (Store → Users → Roles):
- **View your affiliates**: read-only access to all plugin pages.
- **Manage your affiliates**: approve or ban affiliates, change settings, run cycles, approve payouts.

## 3. Program settings (Affiliates → Settings)
| Setting | Meaning |
|---|---|
| Affiliate program enabled | Opens signup, referral links and the panel. |
| Default commission (%) / Pay commission on | The fallback rule: first payment only, every payment (lifetime) or the first N payments of a subscriber. |
| Hold period (days) | Commissions stay *Pending* this long before becoming *Payable*, which leaves time for refunds. |
| Commission base currency | Currency commissions are computed in (blank = store default). The store's rate source needs a BTC pair for it. |
| Approve new affiliates automatically | Otherwise new signups wait as *Pending* until you approve them. |
| Lightning / On-chain payouts | Payout methods affiliates may choose. |
| Approve payouts automatically | Off by default: you review each cycle. |
| Minimum payout (sats) | Smaller balances carry over to the next cycle. |
| Payout cycle / day | Weekly (0 = Sunday … 6) or monthly (1–28). Times are UTC. |
| Landing page URL (Affiliates tab) | Where referral links send visitors; also the only external site allowed as a post-payment redirect. |

**Commission rules per plan** override the default. A rule can be a percentage or a flat amount, and has its own scope. Order of precedence: plan rule → store default rule → default commission.

## 4. Affiliates (Affiliates tab)
- **Signup:** share the public signup page link with prospective affiliates.
- **Actions:**
  - **Approve** a pending affiliate.
  - **Suspend** an affiliate: they keep their balance, earn nothing new, and their payouts are held.
  - **Reactivate** a suspended affiliate.
  - **Ban** an affiliate. This is final and also ends all their panel sessions.
- **Panel link:** if the store has no email set up, give affiliates panel access with **Panel link**. It creates a personal 30-day link and shows it once. Send it privately; issuing a new one cancels the old one.
- **Referral links:** links look like `https://BTCPAY/aff/STORE_ID/CODE`. The first click wins for 30 days. See `docs/INTEGRATION.md` to connect your website and checkout buttons.

## 5. Commissions (Commissions tab)
- **When commissions are created:** when an invoice (one-time or subscription renewal) settles and carries a valid referral. Self-referrals are ignored.
- **Statuses:** *Pending* (holding) → *Payable* → *Paid*, or *Reversed*.
- **Refunds:**
  - A refund or invalidation before payout reverses the commission. A partial refund reverses that share of it.
  - After payout, the same amount is deducted from the next payout as a *clawback*.
  - **Reverse** does this manually.
- **Waiting for an exchange rate:** a payment whose rate couldn't be fetched is retried automatically. **Process holds and retries now** forces a retry.

## 6. Payouts (Payouts tab)
1. On the cycle day a *commissions due* report is created for every affiliate whose net payable balance reaches the minimum. You can also run one now with **Run payout cycle now**.
2. Review the report: check the destinations. Download the CSV if you need it for your accounting.
3. **Approve** (or **Approve all**). This creates a BTCPay store payout to the affiliate's Lightning address/LNURL or Bitcoin address. **Cancel** returns a batch to the balance.
4. BTCPay's payout processors (Store → Payout processors) send it, or you pay it manually from Store → Payouts. When BTCPay marks the payout completed, the commissions become *Paid*.

## 7. Reports
**Reporting** (store menu) gains two views: **Affiliate commissions** and **Affiliate payouts**. They have date filters, charts and CSV **Export**. Text columns are protected against spreadsheet formula injection.

## 7b. Order confirmation callback (Order callback tab)
Notifies your website when a payment settles, so it can confirm the order, with the affiliate (if any) and whether a commission was credited.
1. **Generate secret** and copy it into your site's configuration. It is shown only once; **Regenerate secret** replaces it immediately (update your site at the same time).
2. Enter the **Callback URL** (https, public host), switch on **Send order confirmation callbacks**, **Save**.
3. **Send test event** and check the **Delivery log**: *Delivered* with HTTP 200 means your site verified and accepted it.
4. Failed deliveries are retried automatically (8 attempts over about 2 hours); after that they show *Failed*. Fix your site and click **Redeliver** (same event id, so your site can ignore duplicates).

Only need a plain "invoice settled" notification with no affiliate data? BTCPay's own Store → Webhooks does that too. See `docs/INTEGRATION.md` §5 for the receiver code.

## 8. Language
The plugin ships English and Spanish. It adds its Spanish strings to a BTCPay dictionary named "Spanish" (created if missing).
- **Turn Spanish on:** Server Settings → Translations → *Spanish* → **Select**.
- **Translate the rest of BTCPay:** install BTCPay's Spanish language pack there too. The plugin re-adds its strings within 15 minutes after a pack install or update.
- **Your edits are kept:** if you change a plugin string in the translation editor, the plugin does not overwrite it.

## 9. Security checklist
- Keep manual payout approval, and look at destinations before approving.
- Configure email: affiliates then get sign-in links and a notice whenever their payout address changes.
- Grant "Manage your affiliates" only to people who may approve payouts.
- Keep BTCPay updated. Report vulnerabilities as described in `SECURITY.md`.

## 10. Troubleshooting
| Symptom | Cause / fix |
|---|---|
| Referral link gives 404 | No landing page URL set (Affiliates tab). |
| A sale shows no commission | No referral on the checkout, a self-referral, an affiliate who is not Active, a scope already used up (first payment / first N), or a rate still pending (see Commissions). |
| Affiliate says the sign-in email never arrived | Check store email settings; at most 3 links per 15 minutes; use **Panel link** as a fallback. |
| Batch stuck in "Payout created" | Check Store → Payouts for a payout with `metadata.batchId`. If BTCPay stopped mid-approval, the payout may be missing. Cancel it in BTCPay to release the batch, or contact support. |
| Callback shows *Failed* / HTTP 401 | Your site rejected the signature: wrong secret (regenerated?), or it re-serialized the JSON instead of hashing the raw body, or its clock is off by more than 5 minutes. |
| Callback URL refused on save | Must be https and a public host; localhost, private and link-local addresses are refused. |
| Lightning payout fails | The affiliate's Lightning address/LNURL must be reachable from your BTCPay and your node needs outbound liquidity. |
