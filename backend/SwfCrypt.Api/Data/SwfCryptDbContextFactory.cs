using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace SwfCrypt.Api.Data;

public sealed class SwfCryptDbContextFactory : IDesignTimeDbContextFactory<SwfCryptDbContext>
{
    public SwfCryptDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<SwfCryptDbContext>();
        optionsBuilder.UseSqlite("Data Source=swfcrypt.db");
        return new SwfCryptDbContext(optionsBuilder.Options);
    }
}
