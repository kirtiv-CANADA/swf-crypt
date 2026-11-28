using System.Text.Json;
using SwfCrypt.Api.Data;

namespace SwfCrypt.Api.Endpoints;

public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, int Total);

public sealed record FileSummaryDto(
    Guid Id, string Title, string? Author, int? Year, string License, string Source,
    DateTimeOffset CreatedAt, string? ActionScript, CompatibilityVerdict? Verdict);

public sealed record JobDto(JobState State, int Attempts, string? LastError, DateTimeOffset? StartedAt, DateTimeOffset? FinishedAt);

public sealed record ReportDto(string ActionScript, int SwfVersion, CompatibilityVerdict Verdict, JsonElement Details);

public sealed record FileDetailDto(
    Guid Id, string Title, string? Author, int? Year, string License, string Source,
    long SizeBytes, string Sha256, ReviewStatus Status, DateTimeOffset CreatedAt, JobDto? Job, ReportDto? Report);

public sealed record MyFileDto(
    Guid Id, string Title, ReviewStatus Status, JobState? State, int? Attempts, string? LastError,
    string? ActionScript, CompatibilityVerdict? Verdict, DateTimeOffset CreatedAt);

public sealed record PendingFileDto(
    Guid Id, string Title, string? Author, int? Year, string Source, string License, string? OwnerEmail,
    DateTimeOffset CreatedAt, JobState? State, int? Attempts, string? LastError,
    string? ActionScript, CompatibilityVerdict? Verdict);

public sealed record UploadResponse(Guid Id, ReviewStatus Status, JobState? State, bool Duplicate);

public sealed record ReviewRequest(string? Note);

public sealed record SetRoleRequest(string Role);

public sealed record UserDto(Guid Id, string? Email, string Role);

public sealed record AuditLogDto(
    Guid Id, Guid? UserId, string? UserEmail, string Action, string EntityType,
    string EntityId, DateTimeOffset CreatedAt, string? Details);
