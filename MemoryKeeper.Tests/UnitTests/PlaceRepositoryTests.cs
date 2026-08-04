using MemoryKeeper.Domain.Entities;
using MemoryKeeper.Infrastructure.Database;
using MemoryKeeper.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;

namespace MemoryKeeper.Tests.UnitTests;

public class PlaceRepositoryTests
{
    [Fact]
    public async Task UpdateAsync_AfterAdd_DoesNotThrowTrackingConflict()
    {
        await using var db = CreateDb();
        var repository = new PlaceRepository(db);
        var place = new Place
        {
            Id = Guid.NewGuid(),
            DisplayName = "Home",
            Latitude = 37.5,
            Longitude = 127.0,
            Radius = 100,
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        await repository.AddAsync(place);

        for (var i = 0; i < 3; i++)
        {
            var loaded = await repository.GetByIdAsync(place.Id);
            Assert.NotNull(loaded);
            loaded.Latitude = 37.5 + (i * 0.001);
            loaded.Longitude = 127.0 + (i * 0.001);
            loaded.UpdatedAt = DateTime.UtcNow;
            await repository.UpdateAsync(loaded);
        }

        var final = await repository.GetByIdAsync(place.Id);
        Assert.Equal(37.502, final!.Latitude, 3);
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
