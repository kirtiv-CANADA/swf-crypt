using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using SwfCrypt.Api.Auth;
using SwfCrypt.Api.Data;
using SwfCrypt.Api.Services;

namespace SwfCrypt.Api.Endpoints;

public static class ReviewEndpoints
{
    public static void MapReviewEndpoints(this IEndpointRouteBuilder app)
    {
        var review = app.MapGroup("/api/review")
            .WithTags("Review")
            .RequireAuthorization(Policies.CuratorOrAbove);

        review.MapGet("pending", ListPending);
        review.MapPost("{id:guid}/approve", Approve);
        review.MapPost("{id:guid}/reject", Reject);
    }

    private static async Task<IResult> ListPending(
        SwfCryptDbContext db, CancellationToken ct, int page = 1, int pageSize = 20)
    {
        (page, pageSize) = Paging.Normalize(page, pageSize);

        var query = db.SwfFiles.AsNoTracking().Where(f => f.ReviewStatus == ReviewStatus.Pending);

        var total = await query.CountAsync(ct);
        var items = await query
            .OrderBy(f => f.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(f => new PendingFileDto(
                f.Id, f.Title, f.Author, f.Year, f.Source, f.License,
                f.Owner == null ? null : f.Owner.Email,
                f.CreatedAt,
                f.AnalysisJob == null ? null : (JobState?)f.AnalysisJob.State,
                f.AnalysisJob == null ? null : (int?)f.AnalysisJob.Attempts,
                f.AnalysisJob == null ? null : f.AnalysisJob.LastError,
                f.AutopsyReport == null ? null : f.AutopsyReport.ActionScriptKind,
                f.AutopsyReport == null ? null : (CompatibilityVerdict?)f.AutopsyReport.Verdict))
            .ToListAsync(ct);

        return Results.Ok(new PagedResult<PendingFileDto>(items, page, pageSize, total));
    }

    private static async Task<IResult> Approve(
        Guid id, ReviewRequest? body, SwfCryptDbContext db, ClaimsPrincipal user, CancellationToken ct)
    {
        var note = body?.Note?.Trim();
        if (note is { Length: > 1000 })
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["note"] = ["Note must be 1000 characters or fewer."] });
        }

        var file = await db.SwfFiles.Include(f => f.AutopsyReport).FirstOrDefaultAsync(f => f.Id == id, ct);
        if (file is null)
        {
            return Results.NotFound();
        }

        if (file.ReviewStatus != ReviewStatus.Pending)
        {
            return Results.Problem(title: "Already reviewed", detail: $"This file is already {file.ReviewStatus}.", statusCode: StatusCodes.Status409Conflict);
        }

        if (file.AutopsyReport is null)
        {
            return Results.Problem(
                title: "Analysis not finished",
                detail: "A file can only be approved after its autopsy report exists.",
                statusCode: StatusCodes.Status409Conflict);
        }

        return await DecideAsync(db, file, user, ReviewStatus.Approved, note, ct);
    }

    private static async Task<IResult> Reject(
        Guid id, ReviewRequest? body, SwfCryptDbContext db, ClaimsPrincipal user, CancellationToken ct)
    {
        var note = body?.Note?.Trim();
        if (string.IsNullOrWhiteSpace(note))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["note"] = ["A note is required when rejecting a file."] });
        }

        if (note.Length > 1000)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["note"] = ["Note must be 1000 characters or fewer."] });
        }

        var file = await db.SwfFiles.FirstOrDefaultAsync(f => f.Id == id, ct);
        if (file is null)
        {
            return Results.NotFound();
        }

        if (file.ReviewStatus != ReviewStatus.Pending)
        {
            return Results.Problem(title: "Already reviewed", detail: $"This file is already {file.ReviewStatus}.", statusCode: StatusCodes.Status409Conflict);
        }

        return await DecideAsync(db, file, user, ReviewStatus.Rejected, note, ct);
    }

    private static async Task<IResult> DecideAsync(
        SwfCryptDbContext db, SwfFile file, ClaimsPrincipal user, ReviewStatus decision, string? note, CancellationToken ct)
    {
        var rawReviewerId = user.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(rawReviewerId, out var reviewerId))
        {
            return Results.Unauthorized();
        }

        file.ReviewStatus = decision;
        db.ReviewDecisions.Add(new ReviewDecision
        {
            SwfFileId = file.Id,
            ReviewerId = reviewerId,
            Decision = decision,
            Note = string.IsNullOrWhiteSpace(note) ? null : note,
            DecidedAt = DateTimeOffset.UtcNow
        });
        Audit.Add(db, reviewerId, decision == ReviewStatus.Approved ? "review.approve" : "review.reject",
            nameof(SwfFile), file.Id.ToString(), note);

        await db.SaveChangesAsync(ct);

        return Results.Ok(new { id = file.Id, status = file.ReviewStatus });
    }
}
