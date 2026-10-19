using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BTCPayServer.Plugins.MaxiAffiliates.Services;

/// <summary>Sends due seller callbacks: immediately when one is queued, otherwise polling every 5 s for retries.</summary>
public class CallbackDispatcher(CallbackService callbacks, ILogger<CallbackDispatcher> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try { while (await callbacks.ProcessDue(stoppingToken) > 0) { } }
            catch (Exception e) when (e is not OperationCanceledException) { logger.LogError(e, "MaxiAffiliates: callback dispatch failed"); }
            await callbacks.WaitForWork(TimeSpan.FromSeconds(5), stoppingToken);
        }
    }
}
