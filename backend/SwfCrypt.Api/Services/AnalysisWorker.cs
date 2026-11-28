using Microsoft.EntityFrameworkCore;
using SwfCrypt.Api.Data;

namespace SwfCrypt.Api.Services;

public sealed class AnalysisWorker(
    AnalysisQueue queue,
    IServiceScopeFactory scopes,
    ILogger<AnalysisWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _ = RecoverAsync(stoppingToken);

        try
        {
            await foreach (var jobId in queue.Reader.ReadAllAsync(stoppingToken))
            {
                try
                {
                    await using var scope = scopes.CreateAsyncScope();
                    var processor = scope.ServiceProvider.GetRequiredService<AnalysisProcessor>();
                    await processor.RunAsync(jobId, stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Unexpected error while processing job {JobId}", jobId);
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Normal shutdown.
        }
    }

    private async Task RecoverAsync(CancellationToken ct)
    {
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<SwfCryptDbContext>();
            var recovered = await JobRecovery.RecoverAsync(db, queue, ct);
            if (recovered > 0)
            {
                logger.LogInformation("Re-enqueued {Count} unfinished analysis job(s) after startup", recovered);
            }
        }
        catch (OperationCanceledException)
        {
            // Shutting down.
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Could not recover unfinished analysis jobs");
        }
    }
}

public static class JobRecovery
{
    public static async Task<int> RecoverAsync(SwfCryptDbContext db, AnalysisQueue queue, CancellationToken ct)
    {
        var jobs = await db.AnalysisJobs
            .Where(j => j.State == JobState.Queued || j.State == JobState.Running)
            .OrderBy(j => j.CreatedAt)
            .ToListAsync(ct);

        foreach (var job in jobs)
        {
            job.State = JobState.Queued;
        }

        await db.SaveChangesAsync(ct);

        foreach (var job in jobs)
        {
            await queue.EnqueueAsync(job.Id, ct);
        }

        return jobs.Count;
    }
}
