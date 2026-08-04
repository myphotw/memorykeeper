using MemoryKeeper.Domain.Entities;
using MemoryKeeper.Infrastructure.Database;
using MemoryKeeper.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;

namespace MemoryKeeper.Tests.UnitTests;

public class SettingRepositoryTests
{
    [Fact]
    public async Task UpdateAsync_CanBeCalledRepeatedly_WithoutTrackingConflict()
    {
        await using var db = CreateDb();
        var repository = new SettingRepository(db);
        var setting = new Setting
        {
            Id = Guid.NewGuid(),
            Key = "Travel:HomeAddress",
            Value = "old",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        await repository.AddAsync(setting);

        for (var i = 0; i < 3; i++)
        {
            var loaded = await repository.GetByKeyAsync("Travel:HomeAddress");
            Assert.NotNull(loaded);
            loaded.Value = $"addr-{i}";
            loaded.UpdatedAt = DateTime.UtcNow;
            await repository.UpdateAsync(loaded);
        }

        var final = await repository.GetByKeyAsync("Travel:HomeAddress");
        Assert.Equal("addr-2", final!.Value);
    }

    private static MemoryKeeperDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<MemoryKeeperDbContext>()
            .UseSqlite("Data Source=:memory:")
            .Options;
        var db = new MemoryKeeperDbContext(options);
        db.Database.OpenConnection();
        db.Database.EnsureCreated();
        return db;
    }
}
