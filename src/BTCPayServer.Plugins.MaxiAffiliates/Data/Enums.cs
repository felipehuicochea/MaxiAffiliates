namespace BTCPayServer.Plugins.MaxiAffiliates.Data;

public enum AffiliateStatus { Pending, Active, Suspended, Banned }
public enum PayoutMethodKind { Lightning, OnChain }
public enum CommissionType { Percent, Flat }
/// <summary>Which payments of a referred customer earn commission.</summary>
public enum CommissionScope { FirstPayment, Lifetime, FirstN }
public enum CommissionStatus { Pending, Payable, Paid, Reversed }
public enum PayoutBatchState { Draft, Approved, Sent, Completed, Failed, Cancelled }
public enum PayoutFrequency { Weekly, Monthly }
public enum ReferralSource { Link, QueryParam, Api, Manual }
public enum CallbackDeliveryStatus { Pending, Succeeded, Failed }
public enum CallbackEventKind { InvoiceSettled, Test }
