using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SwfCrypt.Api.Config;
using SwfCrypt.Api.Data;

namespace SwfCrypt.Api.Services;

public sealed class AnalysisProcessor(
    SwfCryptDbContext db,
    ISwfAnalyzer analyzer,
    SwfFileStore store,
    IOptions<AnalysisOptions> options,
    ILogger<AnalysisProcessor> logger)
{
    public async Task RunAsync(Guid jobId, CancellationToken ct)
    {
        var job = await db.AnalysisJobs.Include(j => j.SwfFile).FirstOrDefaultAsync(j => j.Id == jobId, ct);
        if (job is null)
        {
            logger.LogWarning("Analysis job {JobId} no longer exists", jobId);
            return;
        }

        if (job.State == JobState.Succeeded || job.State == JobState.Failed)
        {
            return;
        }

        var settings = options.Value;
        var maxAttempts = Math.Max(1, settings.MaxAttempts);

        while (true)
        {
            job.State = JobState.Running;
            job.Attempts++;
            job.StartedAt ??= DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(ct);

            try
            {
                var bytes = await store.ReadAsync(job.SwfFile.StorageKey, ct);
                var data = await analyzer.AnalyzeAsync(bytes, ct);
                var verdict = CompatibilityPredictor.Predict(data.ActionScript, data.Version);

                var report = await db.AutopsyReports.FirstOrDefaultAsync(r => r.SwfFileId == job.SwfFileId, ct);
                if (report is null)
                {
                    report = new AutopsyReport { SwfFileId = job.SwfFileId };
                    db.AutopsyReports.Add(report);
                }

                report.ActionScriptKind = data.ActionScript;
                report.Version = (byte)Math.Clamp(data.Version, byte.MinValue, byte.MaxValue);
                report.FileSizeBytes = bytes.LongLength;
                report.Compression = bytes.Length >= 3 ? new string(Encoding.ASCII.GetChars(bytes, 0, 3)) : "FWS";
                report.Verdict = verdict;
                report.VerdictReason = $"Predicted from ActionScript '{data.ActionScript}' and SWF v{data.Version}.";
                report.TagSummaryJson = data.ReportJson;
            try
            {
                using var doc = System.Text.Json.JsonDocument.Parse(data.ReportJson);
                var root = doc.RootElement;
                if (root.TryGetProperty("frameRate", out var fr) && fr.TryGetDouble(out var dFr)) report.FrameRate = dFr;
                if (root.TryGetProperty("frameCount", out var fc) && fc.TryGetInt32(out var iFc)) report.FrameCount = iFc;
                if (root.TryGetProperty("totalTagCount", out var tc) && tc.TryGetInt32(out var iTc)) report.TotalTagCount = iTc;
            }
            catch { }
                report.AnalyzedAt = DateTimeOffset.UtcNow;

                job.State = JobState.Succeeded;
                job.LastError = null;
                job.FinishedAt = DateTimeOffset.UtcNow;
                await db.SaveChangesAsync(ct);

                logger.LogInformation("Job {JobId} succeeded after {Attempts} attempt(s)", job.Id, job.Attempts);
                return;
            }
            catch (PermanentAnalysisException ex)
            {
                await FailAsync(job, ex.Message, ct);
                return;
            }
            catch (FileNotFoundException ex)
            {
                await FailAsync(job, "The stored SWF file is missing: " + ex.Message, ct);
                return;
            }
            catch (DirectoryNotFoundException ex)
            {
                await FailAsync(job, "The stored SWF file is missing: " + ex.Message, ct);
                return;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                DiscardUnsavedReports();
                job.LastError = Truncate(ex.Message);

                if (job.Attempts >= maxAttempts)
                {
                    await FailAsync(job, ex.Message, ct);
                    return;
                }

                logger.LogWarning(ex, "Job {JobId} failed on attempt {Attempt}, will retry", job.Id, job.Attempts);

                job.State = JobState.Queued;
                await db.SaveChangesAsync(ct);

                var delay = TimeSpan.FromSeconds(settings.BaseDelaySeconds * Math.Pow(2, job.Attempts - 1));
                if (delay > TimeSpan.Zero)
                {
                    await Task.Delay(delay, ct);
                }
            }
        }
    }

    private async Task FailAsync(AnalysisJob job, string error, CancellationToken ct)
    {
        job.State = JobState.Failed;
        job.LastError = Truncate(error);
        job.FinishedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);

        logger.LogWarning("Job {JobId} failed: {Error}", job.Id, job.LastError);
    }

    private void DiscardUnsavedReports()
    {
        foreach (var entry in db.ChangeTracker.Entries<AutopsyReport>().Where(e => e.State == EntityState.Added).ToList())
        {
            entry.State = EntityState.Detached;
        }
    }

    private static string Truncate(string text) => text.Length <= 500 ? text : text[..500];
}

