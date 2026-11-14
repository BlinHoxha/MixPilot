using MixPilot.Application.Abstractions.Persistence;
using MixPilot.Application.Abstractions.Services;

namespace MixPilot.Api;

public sealed class SetGenerationWorker(IServiceScopeFactory scopes, TimeProvider clock,
    IConfiguration config, ILogger<SetGenerationWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromSeconds(Math.Max(0.1, config.GetValue("Jobs:PollSeconds", 2.0)));
        while (!stoppingToken.IsCancellationRequested)
        {
            JobClaim? claim = null;
            try
            {
                await using (var scope = scopes.CreateAsyncScope())
                    claim = await scope.ServiceProvider.GetRequiredService<IJobClaimRepository>()
                        .ClaimNext(clock.GetUtcNow(), stoppingToken);
                if (claim is { Exhausted: false }) await Process(claim, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception error) { logger.LogError(error, "Set-generation worker iteration failed."); }
            if (claim is null || claim.Exhausted)
            {
                try { await Task.Delay(interval, stoppingToken); }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            }
        }
    }

    private async Task Process(JobClaim claim, CancellationToken stoppingToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<JobOwnerContext>().SetForWorker(claim.OwnerKey);
        using var leaseCancellation = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        var heartbeat = KeepLease(claim, leaseCancellation);
        try
        {
            await scope.ServiceProvider.GetRequiredService<ISetGenerationJobProcessor>()
                .Process(claim.Id, claim.Attempt, leaseCancellation.Token);
        }
        finally
        {
            await leaseCancellation.CancelAsync();
            await heartbeat;
        }
    }

    private async Task KeepLease(JobClaim claim, CancellationTokenSource cancellation)
    {
        try
        {
            while (!cancellation.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromSeconds(30), cancellation.Token);
                await using var scope = scopes.CreateAsyncScope();
                scope.ServiceProvider.GetRequiredService<JobOwnerContext>().SetForWorker(claim.OwnerKey);
                var renewed = await scope.ServiceProvider.GetRequiredService<ISetGenerationJobRepository>()
                    .RenewLease(claim.Id, claim.Attempt, clock.GetUtcNow(), cancellation.Token);
                if (!renewed) await cancellation.CancelAsync();
            }
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
    }
}
