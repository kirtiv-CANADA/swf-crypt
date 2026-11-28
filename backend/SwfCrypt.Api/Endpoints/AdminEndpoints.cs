using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using SwfCrypt.Api.Auth;
using SwfCrypt.Api.Data;
using SwfCrypt.Api.Services;

namespace SwfCrypt.Api.Endpoints;

public static class AdminEndpoints
{
    public static void MapAdminEndpoints(this IEndpointRouteBuilder app)
    {
        var admin = app.MapGroup("/api/admin")
            .WithTags("Admin")
            .RequireAuthorization(Policies.AdminOnly);

        admin.MapGet("users", ListUsers);
        admin.MapPut("users/{id}/role", SetRole);
        admin.MapGet("audit", ListAudit);
    }

    private static async Task<IResult> ListUsers(SwfCryptDbContext db, CancellationToken ct, int page = 1, int pageSize = 20)
    {
        (page, pageSize) = Paging.Normalize(page, pageSize);

        var total = await db.Users.CountAsync(ct);
        var users = await db.Users.AsNoTracking()
            .OrderBy(u => u.Email)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(u => new { u.Id, u.Email })
            .ToListAsync(ct);

        var ids = users.Select(u => u.Id).ToList();
        var roleRows = await (
            from userRole in db.UserRoles
            join role in db.Roles on userRole.RoleId equals role.Id
            where ids.Contains(userRole.UserId)
            select new { userRole.UserId, role.Name }).ToListAsync(ct);

        var items = users.Select(u =>
        {
            var names = roleRows.Where(r => r.UserId == u.Id).Select(r => r.Name).ToList();
            var role = names.Contains(Roles.Admin) ? Roles.Admin
                : names.Contains(Roles.Curator) ? Roles.Curator
                : Roles.Member;
            return new UserDto(u.Id, u.Email, role);
        }).ToList();

        return Results.Ok(new PagedResult<UserDto>(items, page, pageSize, total));
    }

    private static async Task<IResult> SetRole(
        string id, SetRoleRequest body, UserManager<AppUser> users, SwfCryptDbContext db, ClaimsPrincipal caller, CancellationToken ct)
    {
        if (body.Role != Roles.Member && body.Role != Roles.Curator && body.Role != Roles.Admin)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["role"] = [$"Role must be {Roles.Member}, {Roles.Curator} or {Roles.Admin}."]
            });
        }

        if (!Guid.TryParse(id, out var targetUserId))
        {
            return Results.NotFound();
        }

        var callerId = caller.FindFirstValue(ClaimTypes.NameIdentifier);
        if (Guid.TryParse(callerId, out var callerGuid) && targetUserId == callerGuid && body.Role != Roles.Admin)
        {
            return Results.Problem(
                title: "Not allowed",
                detail: "You cannot remove your own Admin role.",
                statusCode: StatusCodes.Status400BadRequest);
        }

        var user = await users.FindByIdAsync(targetUserId.ToString());
        if (user is null)
        {
            return Results.NotFound();
        }

        var current = await users.GetRolesAsync(user);
        var previous = current.Contains(Roles.Admin) ? Roles.Admin
            : current.Contains(Roles.Curator) ? Roles.Curator
            : Roles.Member;

        var staffRoles = current.Where(r => r == Roles.Curator || r == Roles.Admin).ToList();
        if (staffRoles.Count > 0)
        {
            await users.RemoveFromRolesAsync(user, staffRoles);
        }

        if (body.Role != Roles.Member)
        {
            await users.AddToRoleAsync(user, body.Role);
        }

        Audit.Add(db, Guid.TryParse(callerId, out var auditActor) ? auditActor : null, "user.role", nameof(AppUser), user.Id.ToString(), $"{previous} -> {body.Role}");
        await db.SaveChangesAsync(ct);

        return Results.Ok(new UserDto(user.Id, user.Email, body.Role));
    }

    private static async Task<IResult> ListAudit(SwfCryptDbContext db, CancellationToken ct, int page = 1, int pageSize = 20)
    {
        (page, pageSize) = Paging.Normalize(page, pageSize);

        var total = await db.AuditLogs.CountAsync(ct);

        var items = await (
            from entry in db.AuditLogs.AsNoTracking()
            join user in db.Users on entry.UserId equals user.Id into joined
            from user in joined.DefaultIfEmpty()
            orderby entry.CreatedAt descending, entry.Id descending
            select new AuditLogDto(
                entry.Id, entry.UserId, user == null ? null : user.Email,
                entry.Action, entry.EntityType, entry.EntityId, entry.CreatedAt, entry.DetailsJson))
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return Results.Ok(new PagedResult<AuditLogDto>(items, page, pageSize, total));
    }
}
