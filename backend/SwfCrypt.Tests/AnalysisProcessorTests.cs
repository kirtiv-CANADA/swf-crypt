using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SwfCrypt.Api.Config;
using SwfCrypt.Api.Data;
using SwfCrypt.Api.Services;

namespace SwfCrypt.Tests;

internal sealed class ProcessorHarness : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "swfcrypt-proc-" + Guid.NewGuid().ToString("N"));

    public SwfCryptDbContext Db { get; }
    public SwfFileStore Store { get; }
    public FakeAnalyzer Analyzer { get; } = new();
    public AnalysisQueue Queue { get; }
    public IOptions<AnalysisOptions> Settings { get; }

    public ProcessorHarness(int maxAttempts = 3)
    {
        Directory.CreateDirectory(_root);

        var dbOptions = new DbContextOptionsBuilder<SwfCryptDbContext>()
            .UseSqlite($"Data Source={Path.Combine(_root, "unit.db")}")
            .Options;
        Db = new SwfCryptDbContext(dbOptions);
        Db.Database.EnsureCreated();

        Settings = Microsoft.Extensions.Options.Options.Create(
            new AnalysisOptions { MaxAttempts = maxAttempts, BaseDelaySeconds = 0, QueueCapacity = 100 });
        Store = new SwfFileStore(Microsoft.Extensions.Options.Options.Create(
            new StorageOptions { SwfFolder = Path.Combine(_root, "swf") }));
        Queue = new AnalysisQueue(Settings);
    }

    public AnalysisProcessor NewProcessor() =>
        new(Db, Analyzer, Store, Settings, NullLogger<AnalysisProcessor>.Instance);

    public async Task<Guid> AddJobAsync(JobState state = JobState.Queued, bool writeFile = true)
    {
        var userId = Guid.NewGuid();
        var email = $"{userId:N}@test.local";
        Db.Users.Add(new AppUser { Id = userId, UserName = email, NormalizedUserName = email.ToUpperInvariant(), Email = email });

        var fileId = Guid.NewGuid();
        var storageKey = writeFile
            ? await Store.SaveAsync(fileId, SwfBuilder.Build(), CancellationToken.None)
            : $"{fileId:N}.swf";

        var job = new AnalysisJob { SwfFileId = fileId, State = state };
        Db.SwfFiles.Add(new SwfFile
        {
            Id = fileId,
            OwnerId = userId,
            Sha256 = Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N"),
            Title = "Test",
            Source = "test",
            License = "CC0",
            StorageKey = storageKey,
            AnalysisJob = job
        });

        await Db.SaveChangesAsync();
        return job.Id;
    }

    public async Task<AnalysisJob> GetJobAsync(Guid jobId)
    {
        Db.ChangeTracker.Clear();
        return await Db.AnalysisJobs.AsNoTracking().FirstAsync(j => j.Id == jobId);
    }

    public void Dispose()
    {
        Db.Dispose();
        SqliteConnection.ClearAllPools();

        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (Exception)
        {
            // Best effort.
        }
    }
}

public class AnalysisProcessorTests
{
    [Fact]
    public async Task Success_stores_the_report_and_marks_the_job_succeeded()
    {
        using var harness = new ProcessorHarness();
        harness.Analyzer.Handler = (_, _) => new AutopsyData(10, "AS3", "{\"frames\":1}");
        var jobId = await harness.AddJobAsync();

        await harness.NewProcessor().RunAsync(jobId, CancellationToken.None);

        var job = await harness.GetJobAsync(jobId);
        Assert.Equal(JobState.Succeeded, job.State);
        Assert.Equal(1, job.Attempts);
        Assert.NotNull(job.FinishedAt);

        var report = await harness.Db.AutopsyReports.AsNoTracking().SingleAsync();
        Assert.Equal("AS3", report.ActionScriptKind);
        Assert.Equal(CompatibilityVerdict.Haunted, report.Verdict);
        Assert.Equal("{\"frames\":1}", report.TagSummaryJson);
    }

    [Fact]
    public async Task A_transient_failure_is_retried_and_then_succeeds()
    {
        using var harness = new ProcessorHarness();
        harness.Analyzer.Handler = (_, call) => call == 1
            ? throw new TransientAnalysisException("temporary glitch")
            : new AutopsyData(10, "AS2", "{}");
        var jobId = await harness.AddJobAsync();

        await harness.NewProcessor().RunAsync(jobId, CancellationToken.None);

        var job = await harness.GetJobAsync(jobId);
        Assert.Equal(JobState.Succeeded, job.State);
        Assert.Equal(2, job.Attempts);
        Assert.Equal(2, harness.Analyzer.Calls);
        Assert.Null(job.LastError);
    }

    [Fact]
    public async Task A_transient_failure_that_never_recovers_fails_after_the_maximum_attempts()
    {
        using var harness = new ProcessorHarness(maxAttempts: 3);
        harness.Analyzer.Handler = (_, _) => throw new TransientAnalysisException("still down");
        var jobId = await harness.AddJobAsync();

        await harness.NewProcessor().RunAsync(jobId, CancellationToken.None);

        var job = await harness.GetJobAsync(jobId);
        Assert.Equal(JobState.Failed, job.State);
        Assert.Equal(3, job.Attempts);
        Assert.Equal(3, harness.Analyzer.Calls);
        Assert.Contains("still down", job.LastError);
        Assert.Equal(0, await harness.Db.AutopsyReports.CountAsync());
    }

    [Fact]
    public async Task A_permanent_failure_fails_immediately_without_a_retry()
    {
        using var harness = new ProcessorHarness();
        harness.Analyzer.Handler = (_, _) => throw new PermanentAnalysisException("bad file");
        var jobId = await harness.AddJobAsync();

        await harness.NewProcessor().RunAsync(jobId, CancellationToken.None);

        var job = await harness.GetJobAsync(jobId);
        Assert.Equal(JobState.Failed, job.State);
        Assert.Equal(1, job.Attempts);
        Assert.Equal(1, harness.Analyzer.Calls);
        Assert.Contains("bad file", job.LastError);
    }

    [Fact]
    public async Task A_missing_stored_file_is_a_permanent_failure()
    {
        using var harness = new ProcessorHarness();
        var jobId = await harness.AddJobAsync(writeFile: false);

        await harness.NewProcessor().RunAsync(jobId, CancellationToken.None);

        var job = await harness.GetJobAsync(jobId);
        Assert.Equal(JobState.Failed, job.State);
        Assert.Equal(1, job.Attempts);
        Assert.Equal(0, harness.Analyzer.Calls);
    }

    [Fact]
    public async Task A_finished_job_is_never_processed_twice()
    {
        using var harness = new ProcessorHarness();
        var jobId = await harness.AddJobAsync();

        await harness.NewProcessor().RunAsync(jobId, CancellationToken.None);
        await harness.NewProcessor().RunAsync(jobId, CancellationToken.None);

        Assert.Equal(1, harness.Analyzer.Calls);
        Assert.Equal(1, (await harness.GetJobAsync(jobId)).Attempts);
    }

    [Fact]
    public async Task Startup_recovery_requeues_unfinished_jobs_only()
    {
        using var harness = new ProcessorHarness();
        var queued = await harness.AddJobAsync(JobState.Queued);
        var running = await harness.AddJobAsync(JobState.Running);
        var succeeded = await harness.AddJobAsync(JobState.Succeeded);

        var recovered = await JobRecovery.RecoverAsync(harness.Db, harness.Queue, CancellationToken.None);

        Assert.Equal(2, recovered);
        Assert.Equal(JobState.Queued, (await harness.GetJobAsync(running)).State);
        Assert.Equal(JobState.Succeeded, (await harness.GetJobAsync(succeeded)).State);

        var enqueued = new HashSet<Guid>();
        while (harness.Queue.Reader.TryRead(out var id))
        {
            enqueued.Add(id);
        }

        Assert.Equal(new HashSet<Guid> { queued, running }, enqueued);
    }
}

