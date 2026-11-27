using Microsoft.AspNetCore.Identity;

namespace SwfCrypt.Api.Data;

public class AppUser : IdentityUser<Guid>
{
    public string? DisplayName { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public ICollection<SwfFile> UploadedFiles { get; set; } = [];
    public ICollection<ReviewDecision> ReviewDecisions { get; set; } = [];
    public ICollection<AuditLog> AuditLogs { get; set; } = [];
}

public enum ReviewStatus
{
    Pending,
    Approved,
    Rejected
}

public enum JobState
{
    Queued,
    Running,
    Succeeded,
    Failed
}

public enum CompatibilityVerdict
{
    Resurrected,
    Haunted,
    BeyondRepair
}

public class SwfFile
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OwnerId { get; set; }
    public AppUser Owner { get; set; } = null!;

    public string Sha256 { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string? Author { get; set; }
    public int? Year { get; set; }
    public string Source { get; set; } = string.Empty;
    public string License { get; set; } = string.Empty;
    public string StorageKey { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    public ReviewStatus ReviewStatus { get; set; } = ReviewStatus.Pending;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    public AnalysisJob? AnalysisJob { get; set; }
    public AutopsyReport? AutopsyReport { get; set; }
    public ICollection<ReviewDecision> ReviewDecisions { get; set; } = [];
}

public class AnalysisJob
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid SwfFileId { get; set; }
    public SwfFile SwfFile { get; set; } = null!;

    public JobState State { get; set; } = JobState.Queued;
    public int Attempts { get; set; }
    public string? LastError { get; set; }
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? FinishedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public class AutopsyReport
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid SwfFileId { get; set; }
    public SwfFile SwfFile { get; set; } = null!;

    public byte Version { get; set; }
    public string Compression { get; set; } = string.Empty;
    public long FileSizeBytes { get; set; }
    public double FrameRate { get; set; }
    public int FrameCount { get; set; }
    public string ActionScriptKind { get; set; } = string.Empty;
    public int TotalTagCount { get; set; }
    public string? TagSummaryJson { get; set; }
    public string? WarningsJson { get; set; }
    public CompatibilityVerdict Verdict { get; set; }
    public string VerdictReason { get; set; } = string.Empty;
    public DateTimeOffset AnalyzedAt { get; set; } = DateTimeOffset.UtcNow;
}

public class ReviewDecision
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid SwfFileId { get; set; }
    public SwfFile SwfFile { get; set; } = null!;

    public Guid ReviewerId { get; set; }
    public AppUser Reviewer { get; set; } = null!;

    public ReviewStatus Decision { get; set; }
    public string? Note { get; set; }
    public DateTimeOffset DecidedAt { get; set; } = DateTimeOffset.UtcNow;
}

public class AuditLog
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid? UserId { get; set; }
    public AppUser? User { get; set; }

    public string Action { get; set; } = string.Empty;
    public string EntityType { get; set; } = string.Empty;
    public string EntityId { get; set; } = string.Empty;
    public string? DetailsJson { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
