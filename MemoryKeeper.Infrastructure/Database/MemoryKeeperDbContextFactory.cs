using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace MemoryKeeper.Infrastructure.Database;

/// <summary>
/// Enables EF Core CLI migrations for MemoryKeeperDbContext.
/// </summary>
public sealed class MemoryKeeperDbContextFactory : IDesignTimeDbContextFactory<MemoryKeeperDbContext>
{
    public MemoryKeeperDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<MemoryKeeperDbContext>();
        optionsBuilder.UseSqlite(SqliteConnectionFactory.CreateConnectionString());

        return new MemoryKeeperDbContext(optionsBuilder.Options);
    }
}
