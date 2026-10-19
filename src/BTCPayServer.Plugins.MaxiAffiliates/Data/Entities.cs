using System;
using System.Collections.Generic;

namespace BTCPayServer.Plugins.MaxiAffiliates.Data;

public static class Ids
{
    public static string New() => Guid.NewGuid().ToString("N");
}

public class Affiliate
{
    public string Id { get; set; } = Ids.New();
    public string StoreId { get; set; } = null!;
    public string Email { get; set; } = null!;
    public string? Name { get; set; }
    /// <summary>Public referral code, unique per store (case-insensitive; stored upper-case).</summary>
    public string Code { get; set; } = null!;
    public AffiliateStatus Status { get; set; } = AffiliateStatus.Pending;
    public PayoutMethodKind PayoutMethod { get; set; }
    /// <summary>LN address / LNURL or BTC address.</summary>
    public string PayoutDestination { get; set; } = null!;
    public DateTimeOffset? PayoutDestinationUpdatedAt { get; set; }
    /// <summary>Referral link clicks (/aff/{store}/{code}) while active.</summary>
    public long LinkClicks { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public List<Referral> Referrals { get; set; } = new();
    public List<Commission> Commissions { get; set; } = new();
}

public class CommissionRule
{
    public string Id { get; set; } = Ids.New();
    public string StoreId { get; set; } = null!;
    /// <summary>Subscription plan id or product key; null = store-wide default.</summary>
    public string? PlanId { get; set; }
    public CommissionType Type { get; set; } = CommissionType.Percent;
    /// <summary>Percent (0-100) or flat amount in the store's commission base currency.</summary>
    public decimal Value { get; set; }
    public CommissionScope Scope { get; set; } = CommissionScope.Lifetime;
    /// <summary>Only for <see cref="CommissionScope.FirstN"/>.</summary>
    public int? FirstN { get; set; }
    public bool Enabled { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>Binding of a customer to an affiliate (attribution). One per customer per store.</summary>
public class Referral
{
    public string Id { get; set; } = Ids.New();
    public string StoreId { get; set; } = null!;
    public string AffiliateId { get; set; } = null!;
    public Affiliate Affiliate { get; set; } = null!;
    /// <summary>Stable customer key: subscriber/customer id when known, otherwise normalized email.</summary>
    public string CustomerKey { get; set; } = null!;
    public string? SubscriberId { get; set; }
    public string? FirstInvoiceId { get; set; }
    public ReferralSource Source { get; set; }
    public DateTimeOffset AttributedAt { get; set; }
    /// <summary>Number of credited payments so far (drives FirstPayment / FirstN scope).</summary>
    public int CreditedPayments { get; set; }
    public List<Commission> Commissions { get; set; } = new();
}

public class Commission
{
    public string Id { get; set; } = Ids.New();
    public string StoreId { get; set; } = null!;
    public string AffiliateId { get; set; } = null!;
    public Affiliate Affiliate { get; set; } = null!;
    public string ReferralId { get; set; } = null!;
    public Referral Referral { get; set; } = null!;
    /// <summary>One commission per invoice (unique per store).</summary>
    public string InvoiceId { get; set; } = null!;
    public string? RuleId { get; set; }
    public string? PlanId { get; set; }
    /// <summary>Invoice price and currency as sold (used to size partial refunds).</summary>
    public decimal SaleAmount { get; set; }
    public string? SaleCurrency { get; set; }
    public decimal BaseAmount { get; set; }
    public string BaseCurrency { get; set; } = null!;
    public decimal CommissionAmount { get; set; }
    /// <summary>Commission in sats at the settlement rate.</summary>
    public long AmountSats { get; set; }
    public decimal Rate { get; set; }
    public CommissionStatus Status { get; set; } = CommissionStatus.Pending;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset PayableAt { get; set; }
    public DateTimeOffset? PaidAt { get; set; }
    public DateTimeOffset? ReversedAt { get; set; }
    public string? ReversedReason { get; set; }
    public string? PayoutBatchId { get; set; }
    public PayoutBatch? PayoutBatch { get; set; }
}

public class PayoutBatch
{
    public string Id { get; set; } = Ids.New();
    public string StoreId { get; set; } = null!;
    public string AffiliateId { get; set; } = null!;
    public Affiliate Affiliate { get; set; } = null!;
    public DateTimeOffset CycleStart { get; set; }
    public DateTimeOffset CycleEnd { get; set; }
    public long AmountSats { get; set; }
    public PayoutMethodKind PayoutMethod { get; set; }
    public string Destination { get; set; } = null!;
    public PayoutBatchState State { get; set; } = PayoutBatchState.Draft;
    public string? PullPaymentId { get; set; }
    public string? PayoutId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? ApprovedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public string? CycleId { get; set; }
    public PayoutCycle? Cycle { get; set; }
    /// <summary>Sum of the commissions in the batch (before clawbacks).</summary>
    public long GrossSats { get; set; }
    /// <summary>Clawbacks netted in this batch (&lt;= 0). AmountSats = GrossSats + AdjustmentSats.</summary>
    public long AdjustmentSats { get; set; }
    public int CommissionCount { get; set; }
    public string? Error { get; set; }
    public List<Commission> Commissions { get; set; } = new();
}

/// <summary>One run of the weekly/monthly payout cycle for a store (or a manual run): the "commissions due" report.</summary>
public class PayoutCycle
{
    public string Id { get; set; } = Ids.New();
    public string StoreId { get; set; } = null!;
    /// <summary>"M-2026-11-01", "W-2026-11-02" or "manual-…"; unique per store so a scheduled cycle runs once.</summary>
    public string CycleKey { get; set; } = null!;
    public DateTimeOffset PeriodEnd { get; set; }
    public DateTimeOffset RunAt { get; set; }
    public bool Manual { get; set; }
    public bool AutoApproved { get; set; }
    public long MinPayoutSats { get; set; }
    public int BatchCount { get; set; }
    public long TotalSats { get; set; }
    /// <summary>Affiliates with a positive balance below the minimum (carried over) at run time.</summary>
    public int CarriedOverCount { get; set; }
    public long CarriedOverSats { get; set; }
    public List<PayoutBatch> Batches { get; set; } = new();
}

/// <summary>Per-store plugin settings. Defaults = Jefe's suggested defaults.</summary>
public class AffiliateStoreSettings
{
    public string StoreId { get; set; } = null!;
    public bool Enabled { get; set; } = true;
    public decimal DefaultPercent { get; set; } = 20m;
    public CommissionScope DefaultScope { get; set; } = CommissionScope.Lifetime;
    /// <summary>Only for DefaultScope = FirstN.</summary>
    public int? DefaultFirstN { get; set; }
    public int HoldDays { get; set; } = 30;
    public bool LightningEnabled { get; set; } = true;
    public bool OnChainEnabled { get; set; } = true;
    public bool AutoApprovePayouts { get; set; }
    public bool AutoApproveAffiliates { get; set; }
    /// <summary>Minimum payable balance before a payout is created; below it the balance carries over.</summary>
    public long MinPayoutSats { get; set; }
    public PayoutFrequency PayoutFrequency { get; set; } = PayoutFrequency.Monthly;
    /// <summary>Weekly: 0=Sunday..6; Monthly: 1..28.</summary>
    public int PayoutDay { get; set; } = 1;
    /// <summary>Null = use the store's default currency.</summary>
    public string? BaseCurrencyOverride { get; set; }
    /// <summary>Where /aff/{storeId}/{code} redirects (with ?ref=CODE).</summary>
    public string? LandingUrl { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>
/// Negative ledger entry clawing back (part of) a commission: partial refunds, or refunds after the commission was paid.
/// Netted against the affiliate's next payable balance; a negative net balance carries over.
/// </summary>
public class CommissionAdjustment
{
    public string Id { get; set; } = Ids.New();
    public string StoreId { get; set; } = null!;
    public string AffiliateId { get; set; } = null!;
    public string CommissionId { get; set; } = null!;
    public Commission Commission { get; set; } = null!;
    /// <summary>Always &lt;= 0.</summary>
    public long AmountSats { get; set; }
    public string Reason { get; set; } = null!;
    /// <summary>Idempotency key, e.g. "refund:{pullPaymentId}", "invalid:{invoiceId}", "credit-refund:{pullPaymentId}".</summary>
    public string SourceRef { get; set; } = null!;
    public DateTimeOffset CreatedAt { get; set; }
    public string? PayoutBatchId { get; set; }
}

/// <summary>A settled invoice that could not be credited yet because no BTC rate was available; retried with backoff.</summary>
public class PendingCredit
{
    public string StoreId { get; set; } = null!;
    public string InvoiceId { get; set; } = null!;
    public int Attempts { get; set; }
    public DateTimeOffset FirstSeenAt { get; set; }
    public DateTimeOffset NextAttemptAt { get; set; }
    public string? LastError { get; set; }
    public bool GaveUp { get; set; }
}

public enum AccessTokenKind { MagicLink, AdminLink, Session }

/// <summary>Affiliate panel credentials. Only a SHA-256 hash of the random token is stored.</summary>
public class AffiliateAccessToken
{
    public string Id { get; set; } = Ids.New();
    public string StoreId { get; set; } = null!;
    public string AffiliateId { get; set; } = null!;
    public Affiliate Affiliate { get; set; } = null!;
    public AccessTokenKind Kind { get; set; }
    public string TokenHash { get; set; } = null!;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? UsedAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
}

/// <summary>Per-store seller-site order confirmation callback (signed POST on every settled payment).</summary>
public class CallbackSettings
{
    public string StoreId { get; set; } = null!;
    public bool Enabled { get; set; }
    public string? Url { get; set; }
    /// <summary>HMAC secret, encrypted with ASP.NET Data Protection. Never logged, shown once when generated.</summary>
    public string? SecretProtected { get; set; }
    public DateTimeOffset? SecretCreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>One delivery of a callback event (automatic, test or manual redelivery). Retried with backoff.</summary>
public class CallbackDelivery
{
    public string Id { get; set; } = Ids.New();
    public string StoreId { get; set; } = null!;
    /// <summary>Idempotency key shared by every delivery (and redelivery) of the same event.</summary>
    public string EventId { get; set; } = null!;
    public CallbackEventKind Kind { get; set; }
    /// <summary>Set on manual redeliveries: the delivery this one repeats.</summary>
    public string? RedeliveryOfId { get; set; }
    public string InvoiceId { get; set; } = "";
    /// <summary>Event facts captured at settle time (JSON); the final payload is frozen into <see cref="Payload"/> on the first attempt.</summary>
    public string Facts { get; set; } = "{}";
    public string? Payload { get; set; }
    public CallbackDeliveryStatus Status { get; set; }
    public int Attempts { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset NextAttemptAt { get; set; }
    public DateTimeOffset? LastAttemptAt { get; set; }
    public int? LastHttpStatus { get; set; }
    public string? LastError { get; set; }
}
