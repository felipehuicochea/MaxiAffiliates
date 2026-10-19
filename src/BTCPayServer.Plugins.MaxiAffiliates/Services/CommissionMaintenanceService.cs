using System;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Services.Invoices;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BTCPayServer.Plugins.MaxiAffiliates.Services;

public record MaintenanceRun(int Retried, int Credited, int Matured, int Cycles = 0);

/// <summary>Every 10 minutes: retry credits waiting for a rate, mature commissions whose hold ended, run due payout cycles.</summary>
public class CommissionMaintenanceService(
    CommissionEngine engine, CommissionLedger ledger, PayoutCycleService cycles, InvoiceCommissionMapper mapper, InvoiceRepository invoices,
    ILogger<CommissionMaintenanceService> logger) : BackgroundService
{
    public static readonly TimeSpan Interval = TimeSpan.FromMinutes(10);
    readonly SemaphoreSlim _lock = new(1, 1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try { await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken); } catch (OperationCanceledException) { return; }
        using var timer = new PeriodicTimer(Interval);
        do
        {
            try { await RunOnce(null, stoppingToken); }
            catch (Exception e) when (e is not OperationCanceledException) { logger.LogError(e, "MaxiAffiliates: maintenance run failed"); }
        } while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false));
    }

    public async Task<MaintenanceRun> RunOnce(string? storeId, CancellationToken ct)
    {
        await _lock.WaitAsync(ct);
        try
        {
            int retried = 0, credited = 0;
            foreach (var p in await ledger.DueRetries(50, ct))
            {
                if (storeId is not null && p.StoreId != storeId) continue;
                retried++;
                var inv = await invoices.GetInvoice(p.InvoiceId);
                if (inv is null || await mapper.Map(inv) is not { } settled) continue;
                var r = await engine.CreditSettledInvoice(settled, cur => mapper.Rate(inv, cur, ct), ct);
                if (r.Outcome == CreditOutcome.Credited) credited++;
                logger.LogInformation("MaxiAffiliates: retry invoice {Invoice} -> {Outcome}", p.InvoiceId, r.Outcome);
            }
            var matured = await ledger.MatureDue(storeId, ct);
            if (matured > 0) logger.LogInformation("MaxiAffiliates: {Count} commissions became payable", matured);
            var open = await cycles.OpenPayoutIds(storeId, ct);
            if (open.Length > 0)
                foreach (var (id, st) in await mapper.PayoutStates(open, ct))
                    await cycles.OnPayoutState(id, st, ct);
            var ran = storeId is null ? await cycles.RunScheduled(ct) : 0; // scheduled cycles only from the timer
            return new(retried, credited, matured, ran);
        }
        finally { _lock.Release(); }
    }
}
