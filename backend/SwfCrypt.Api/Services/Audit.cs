using SwfCrypt.Api.Data;

namespace SwfCrypt.Api.Services;

public static class Audit
{
    public static void Add(SwfCryptDbContext db, Guid? userId, string action, string entityType, string entityId, string? details = null) =>
        db.AuditLogs.Add(new AuditLog
        {
            UserId = userId,
            Action = action,
            EntityType = entityType,
            EntityId = entityId,
            CreatedAt = DateTimeOffset.UtcNow,
            DetailsJson = details
        });
}
