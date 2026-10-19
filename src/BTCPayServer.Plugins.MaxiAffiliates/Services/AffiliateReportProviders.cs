#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Client.Models;
using BTCPayServer.Plugins.MaxiAffiliates.Data;
using BTCPayServer.Services;
using BTCPayServer.Services.Reporting;
using Microsoft.EntityFrameworkCore;

namespace BTCPayServer.Plugins.MaxiAffiliates.Services;

/// <summary>BTCPay Reporting page: "Affiliate commissions" (filter by date, CSV export built in).</summary>
public class AffiliateCommissionsReportProvider(IDbContextFactory<MaxiAffiliatesDbContext> dbFactory, DisplayFormatter displayFormatter) : ReportProvider
{
    public override string Name => "Affiliate commissions";

    public override async Task Query(QueryContext q, CancellationToken cancellation)
    {
        q.ViewDefinition = new ViewDefinition
        {
            Fields = new List<StoreReportResponse.Field>
            {
                new("Date", "datetime"), new("Affiliate", "text"), new("Code", "text"), new("InvoiceId", "invoice_id"),
                new("PlanId", "text"), new("Sale", "amount"), new("Commission", "amount"), new("CommissionSats", "number"),
                new("ClawbackSats", "number"), new("Status", "text"), new("PayableAt", "datetime"), new("PaidAt", "datetime"),
                new("PayoutId", "text"),
            },
            Charts =
            {
                new() { Name = "Commissions by affiliate", Groups = { "Affiliate", "Status" }, Aggregates = { "CommissionSats", "ClawbackSats" }, HasGrandTotal = true }
            }
        };
        await using var db = await dbFactory.CreateDbContextAsync(cancellation);
        var rows = await db.Commissions.AsNoTracking().Include(c => c.Affiliate).Include(c => c.PayoutBatch)
            .Where(c => c.StoreId == q.StoreId && c.CreatedAt >= q.From && c.CreatedAt <= q.To)
            .OrderBy(c => c.CreatedAt).ToListAsync(cancellation);
        var ids = rows.Select(r => r.Id).ToList();
        var adj = await db.CommissionAdjustments.AsNoTracking().Where(a => ids.Contains(a.CommissionId))
            .GroupBy(a => a.CommissionId).Select(g => new { g.Key, S = g.Sum(a => a.AmountSats) }).ToDictionaryAsync(x => x.Key, x => x.S, cancellation);
        foreach (var c in rows)
        {
            var d = q.AddData();
            d.Add(c.CreatedAt);
            d.Add(c.Affiliate.Email);
            d.Add(c.Affiliate.Code);
            d.Add(c.InvoiceId);
            d.Add(c.PlanId);
            d.Add(c.SaleCurrency is null ? null : displayFormatter.ToFormattedAmount(c.SaleAmount, c.SaleCurrency));
            d.Add(displayFormatter.ToFormattedAmount(c.CommissionAmount, c.BaseCurrency));
            d.Add(c.AmountSats);
            d.Add(adj.GetValueOrDefault(c.Id));
            d.Add(c.Status.ToString());
            d.Add(c.PayableAt);
            d.Add(c.PaidAt);
            d.Add(c.PayoutBatch?.PayoutId);
        }
    }
}

/// <summary>BTCPay Reporting page: "Affiliate payouts".</summary>
public class AffiliatePayoutsReportProvider(IDbContextFactory<MaxiAffiliatesDbContext> dbFactory) : ReportProvider
{
    public override string Name => "Affiliate payouts";

    public override async Task Query(QueryContext q, CancellationToken cancellation)
    {
        q.ViewDefinition = new ViewDefinition
        {
            Fields = new List<StoreReportResponse.Field>
            {
                new("Date", "datetime"), new("Affiliate", "text"), new("Code", "text"), new("Method", "text"), new("Destination", "text"),
                new("Commissions", "number"), new("GrossSats", "number"), new("ClawbackSats", "number"), new("AmountSats", "number"),
                new("State", "text"), new("ApprovedAt", "datetime"), new("CompletedAt", "datetime"), new("PayoutId", "text"),
            },
            Charts =
            {
                new() { Name = "Payouts by affiliate", Groups = { "Affiliate", "State" }, Aggregates = { "AmountSats" }, HasGrandTotal = true }
            }
        };
        await using var db = await dbFactory.CreateDbContextAsync(cancellation);
        var rows = await db.PayoutBatches.AsNoTracking().Include(b => b.Affiliate)
            .Where(b => b.StoreId == q.StoreId && b.CreatedAt >= q.From && b.CreatedAt <= q.To)
            .OrderBy(b => b.CreatedAt).ToListAsync(cancellation);
        foreach (var b in rows)
        {
            var d = q.AddData();
            d.Add(b.CreatedAt); d.Add(b.Affiliate.Email); d.Add(b.Affiliate.Code); d.Add(b.PayoutMethod.ToString()); d.Add(b.Destination);
            d.Add(b.CommissionCount); d.Add(b.GrossSats); d.Add(b.AdjustmentSats); d.Add(b.AmountSats);
            d.Add(b.State.ToString()); d.Add(b.ApprovedAt); d.Add(b.CompletedAt); d.Add(b.PayoutId);
        }
    }
}
