using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SwfCrypt.Api.Auth;
using SwfCrypt.Api.Config;
using SwfCrypt.Api.Data;
using SwfCrypt.Api.Services;

namespace SwfCrypt.Api.Endpoints;

public static class FileEndpoints
{
    public static void MapFileEndpoints(this IEndpointRouteBuilder app)
    {
        var files = app.MapGroup("/api/files").WithTags("Files");

        files.MapGet("", ListApproved);
        files.MapGet("{id:guid}", GetById);
        files.MapGet("{id:guid}/content", GetContent);

        files.MapPost("", Upload)
            .RequireAuthorization(Policies.MemberOrAbove)
            .RequireRateLimiting("upload")
            .DisableAntiforgery();

        app.MapGet("/api/me/files", ListMine)
            .WithTags("Files")
            .RequireAuthorization(Policies.MemberOrAbove);
    }

    private static async Task<IResult> ListApproved(
        SwfCryptDbContext db,
        CancellationToken ct,
        int page = 1,
        int pageSize = 20,
        string? q = null,
        string? actionScript = null,
        string? verdict = null,
        int? year = null)
    {
        (page, pageSize) = Paging.Normalize(page, pageSize);

        var query = db.SwfFiles.AsNoTracking().Where(f => f.ReviewStatus == ReviewStatus.Approved);

        if (!string.IsNullOrWhiteSpace(q))
        {
            var term = $"%{q.Trim()}%";
            query = query.Where(f => EF.Functions.Like(f.Title, term) ||
                                     (f.Author != null && EF.Functions.Like(f.Author, term)));
        }

        if (year is not null)
        {
            query = query.Where(f => f.Year == year);
        }

        if (!string.IsNullOrWhiteSpace(actionScript))
        {
            var script = actionScript.Trim();
            query = query.Where(f => f.AutopsyReport != null && f.AutopsyReport.ActionScriptKind == script);
        }

        if (Enum.TryParse<CompatibilityVerdict>(verdict, ignoreCase: true, out var parsedVerdict))
        {
            query = query.Where(f => f.AutopsyReport != null && f.AutopsyReport.Verdict == parsedVerdict);
        }

        var total = await query.CountAsync(ct);
        var items = await query
            .OrderByDescending(f => f.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(f => new FileSummaryDto(
                f.Id, f.Title, f.Author, f.Year, f.License, f.Source, f.CreatedAt,
                f.AutopsyReport == null ? null : f.AutopsyReport.ActionScriptKind,
                f.AutopsyReport == null ? null : (CompatibilityVerdict?)f.AutopsyReport.Verdict))
            .ToListAsync(ct);

        return Results.Ok(new PagedResult<FileSummaryDto>(items, page, pageSize, total));
    }

    private static async Task<IResult> GetById(Guid id, SwfCryptDbContext db, ClaimsPrincipal user, CancellationToken ct)
    {
        var file = await db.SwfFiles.AsNoTracking()
            .Include(f => f.AnalysisJob)
            .Include(f => f.AutopsyReport)
            .FirstOrDefaultAsync(f => f.Id == id, ct);

        if (file is null || !CanView(file, user))
        {
            return Results.NotFound();
        }

        var privileged = IsOwnerOrStaff(file, user);

        var job = privileged && file.AnalysisJob is not null
            ? new JobDto(file.AnalysisJob.State, file.AnalysisJob.Attempts, file.AnalysisJob.LastError, file.AnalysisJob.StartedAt, file.AnalysisJob.FinishedAt)
            : null;

        var report = file.AutopsyReport is null
            ? null
            : new ReportDto(
                file.AutopsyReport.ActionScriptKind,
                file.AutopsyReport.Version,
                file.AutopsyReport.Verdict,
                ParseDetails(file.AutopsyReport.TagSummaryJson ?? "{}"));

        return Results.Ok(new FileDetailDto(
            file.Id, file.Title, file.Author, file.Year, file.License, file.Source,
            file.SizeBytes, file.Sha256, file.ReviewStatus, file.CreatedAt, job, report));
    }

    private static async Task<IResult> GetContent(
        Guid id, SwfCryptDbContext db, SwfFileStore store, ClaimsPrincipal user, CancellationToken ct)
    {
        var file = await db.SwfFiles.AsNoTracking().FirstOrDefaultAsync(f => f.Id == id, ct);
        if (file is null || !CanView(file, user))
        {
            return Results.NotFound();
        }

        try
        {
            var bytes = await store.ReadAsync(file.StorageKey, ct);
            return Results.File(bytes, "application/x-shockwave-flash");
        }
        catch (FileNotFoundException)
        {
            return Results.NotFound();
        }
    }

    private static async Task<IResult> ListMine(
        SwfCryptDbContext db, ClaimsPrincipal user, CancellationToken ct, int page = 1, int pageSize = 20)
    {
        (page, pageSize) = Paging.Normalize(page, pageSize);
        var rawId = user.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(rawId, out var userId))
        {
            return Results.Unauthorized();
        }

        var query = db.SwfFiles.AsNoTracking().Where(f => f.OwnerId == userId);

        var total = await query.CountAsync(ct);
        var items = await query
            .OrderByDescending(f => f.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(f => new MyFileDto(
                f.Id, f.Title, f.ReviewStatus,
                f.AnalysisJob == null ? null : (JobState?)f.AnalysisJob.State,
                f.AnalysisJob == null ? null : (int?)f.AnalysisJob.Attempts,
                f.AnalysisJob == null ? null : f.AnalysisJob.LastError,
                f.AutopsyReport == null ? null : f.AutopsyReport.ActionScriptKind,
                f.AutopsyReport == null ? null : (CompatibilityVerdict?)f.AutopsyReport.Verdict,
                f.CreatedAt))
            .ToListAsync(ct);

        return Results.Ok(new PagedResult<MyFileDto>(items, page, pageSize, total));
    }

    private static async Task<IResult> Upload(
        HttpRequest request,
        ClaimsPrincipal user,
        SwfCryptDbContext db,
        AnalysisQueue queue,
        SwfFileStore store,
        IOptions<StorageOptions> storageOptions,
        CancellationToken ct)
    {
        if (!request.HasFormContentType)
        {
            return Results.Problem(
                title: "Unsupported content type",
                detail: "Send the upload as multipart/form-data.",
                statusCode: StatusCodes.Status415UnsupportedMediaType);
        }

        var form = await request.ReadFormAsync(ct);
        var file = form.Files.GetFile("file");
        var maxBytes = storageOptions.Value.MaxUploadBytes;

        var errors = new Dictionary<string, string[]>();

        var title = form["title"].ToString().Trim();
        var author = form["author"].ToString().Trim();
        var source = form["source"].ToString().Trim();
        var license = form["license"].ToString().Trim();

        if (title.Length == 0) errors["title"] = ["Title is required."];
        else if (title.Length > 200) errors["title"] = ["Title must be 200 characters or fewer."];

        if (author.Length > 160) errors["author"] = ["Author must be 160 characters or fewer."];

        if (source.Length == 0) errors["source"] = ["Source is required (where the file came from)."];
        else if (source.Length > 300) errors["source"] = ["Source must be 300 characters or fewer."];

        if (license.Length == 0) errors["license"] = ["License is required (for example CC0, public domain, own work)."];
        else if (license.Length > 160) errors["license"] = ["License must be 160 characters or fewer."];

        int? year = null;
        var yearText = form["year"].ToString().Trim();
        if (yearText.Length > 0)
        {
            if (!int.TryParse(yearText, out var parsedYear) || parsedYear < 1990 || parsedYear > DateTime.UtcNow.Year)
            {
                errors["year"] = [$"Year must be between 1990 and {DateTime.UtcNow.Year}."];
            }
            else
            {
                year = parsedYear;
            }
        }

        byte[]? bytes = null;

        if (file is null || file.Length == 0)
        {
            errors["file"] = ["A SWF file is required."];
        }
        else if (file.Length > maxBytes)
        {
            errors["file"] = [$"The file is larger than {maxBytes / (1024 * 1024)} MB."];
        }
        else
        {
            bytes = new byte[file.Length];
            await using var stream = file.OpenReadStream();
            await stream.ReadExactlyAsync(bytes, ct);

            if (!HasSwfSignature(bytes))
            {
                errors["file"] = ["This does not look like a SWF file (expected a FWS, CWS or ZWS signature)."];
            }
        }

        if (errors.Count > 0 || bytes is null)
        {
            return Results.ValidationProblem(errors);
        }

        var hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

        var existing = await FindDuplicateAsync(db, hash, ct);
        if (existing is not null)
        {
            return Results.Ok(existing);
        }

        var rawOwnerId = user.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(rawOwnerId, out var ownerId))
        {
            return Results.Unauthorized();
        }

        var id = Guid.NewGuid();
        var storageKey = await store.SaveAsync(id, bytes, ct);

        var swf = new SwfFile
        {
            Id = id,
            OwnerId = ownerId,
            Sha256 = hash,
            Title = title,
            Author = author.Length == 0 ? null : author,
            Year = year,
            Source = source,
            License = license,
            ReviewStatus = ReviewStatus.Pending,
            SizeBytes = bytes.Length,
            StorageKey = storageKey,
            CreatedAt = DateTimeOffset.UtcNow
        };
        var job = new AnalysisJob { SwfFileId = id, State = JobState.Queued, CreatedAt = DateTimeOffset.UtcNow };
        swf.AnalysisJob = job;

        db.SwfFiles.Add(swf);
        Audit.Add(db, ownerId, "file.upload", nameof(SwfFile), id.ToString(), $"sha256={hash}");

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            store.Delete(storageKey);
            db.ChangeTracker.Clear();

            var winner = await FindDuplicateAsync(db, hash, ct);
            if (winner is not null)
            {
                return Results.Ok(winner);
            }

            throw;
        }

        await queue.EnqueueAsync(job.Id, ct);

        return Results.Created($"/api/files/{id}", new UploadResponse(id, swf.ReviewStatus, job.State, Duplicate: false));
    }

    private static async Task<UploadResponse?> FindDuplicateAsync(SwfCryptDbContext db, string hash, CancellationToken ct)
    {
        var existing = await db.SwfFiles.AsNoTracking()
            .Include(f => f.AnalysisJob)
            .FirstOrDefaultAsync(f => f.Sha256 == hash, ct);

        return existing is null
            ? null
            : new UploadResponse(existing.Id, existing.ReviewStatus, existing.AnalysisJob?.State, Duplicate: true);
    }

    private static bool HasSwfSignature(byte[] bytes)
    {
        if (bytes.Length < 8)
        {
            return false;
        }

        var signature = Encoding.ASCII.GetString(bytes, 0, 3);
        return signature == "FWS" || signature == "CWS" || signature == "ZWS";
    }

    private static bool IsOwnerOrStaff(SwfFile file, ClaimsPrincipal user)
    {
        if (user.Identity?.IsAuthenticated != true)
        {
            return false;
        }

        var callerId = user.FindFirstValue(ClaimTypes.NameIdentifier);
        var isOwner = Guid.TryParse(callerId, out var callerGuid) && file.OwnerId == callerGuid;
        return isOwner || user.IsInRole(Roles.Curator) || user.IsInRole(Roles.Admin);
    }

    private static bool CanView(SwfFile file, ClaimsPrincipal user) =>
        file.ReviewStatus == ReviewStatus.Approved || IsOwnerOrStaff(file, user);

    private static JsonElement ParseDetails(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.Clone();
        }
        catch (JsonException)
        {
            using var empty = JsonDocument.Parse("{}");
            return empty.RootElement.Clone();
        }
    }
}

