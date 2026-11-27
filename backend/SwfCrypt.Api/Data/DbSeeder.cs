using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace SwfCrypt.Api.Data;

public sealed class SeedDemoUserOptions
{
    public string Email { get; init; } = string.Empty;
    public string Password { get; init; } = string.Empty;
    public string Role { get; init; } = "Member";
    public string? DisplayName { get; init; }
}

public static class DbSeeder
{
    public static readonly string[] Roles = ["Member", "Curator", "Admin"];

    public static async Task SeedAsync(
        IServiceProvider services,
        IConfiguration configuration,
        CancellationToken cancellationToken = default)
    {
        using var scope = services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<SwfCryptDbContext>();
        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole<Guid>>>();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();

        if (dbContext.Database.IsRelational())
        {
            await dbContext.Database.MigrateAsync(cancellationToken);
        }
        else
        {
            await dbContext.Database.EnsureCreatedAsync(cancellationToken);
        }

        foreach (var roleName in Roles)
        {
            if (!await roleManager.RoleExistsAsync(roleName))
            {
                var roleResult = await roleManager.CreateAsync(new IdentityRole<Guid>(roleName));
                if (!roleResult.Succeeded)
                {
                    var errors = string.Join("; ", roleResult.Errors.Select(e => e.Description));
                    throw new InvalidOperationException($"Failed to create role '{roleName}': {errors}");
                }
            }
        }

        var demoUsers = configuration.GetSection("Seed:DemoUsers").Get<List<SeedDemoUserOptions>>() ?? [];
        foreach (var demoUser in demoUsers)
        {
            if (string.IsNullOrWhiteSpace(demoUser.Email) || string.IsNullOrWhiteSpace(demoUser.Password))
            {
                continue;
            }

            var targetRole = Roles.FirstOrDefault(
                r => string.Equals(r, demoUser.Role, StringComparison.OrdinalIgnoreCase)) ?? "Member";

            var existing = await userManager.FindByEmailAsync(demoUser.Email);
            if (existing is null)
            {
                var user = new AppUser
                {
                    UserName = demoUser.Email,
                    Email = demoUser.Email,
                    EmailConfirmed = true,
                    DisplayName = demoUser.DisplayName ?? demoUser.Email
                };

                var createResult = await userManager.CreateAsync(user, demoUser.Password);
                if (createResult.Succeeded)
                {
                    await userManager.AddToRoleAsync(user, targetRole);
                }
            }
            else if (!await userManager.IsInRoleAsync(existing, targetRole))
            {
                await userManager.AddToRoleAsync(existing, targetRole);
            }
        }
    }
}
