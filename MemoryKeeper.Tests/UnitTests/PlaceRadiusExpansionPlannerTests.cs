using MemoryKeeper.Application;

namespace MemoryKeeper.Tests.UnitTests;

public sealed class PlaceRadiusExpansionPlannerTests
{
    private const double CenterLatitude = 37.5665;
    private const double CenterLongitude = 126.9780;

    [Fact]
    public void PhotosInsideCurrentRadius_DoNotRequestExpansion()
    {
        var plan = PlaceRadiusExpansionPlanner.Create(
            CenterLatitude,
            CenterLongitude,
            100,
            [Photo("inside.jpg", CenterLatitude + 0.0002, CenterLongitude)]);

        Assert.True(plan.HasGpsPhotos);
        Assert.False(plan.NeedsExpansion);
        Assert.Equal(100, plan.ProposedRadiusMeters);
        Assert.False(Assert.Single(plan.PhotoPoints).IsOutsideCurrentRadius);
    }

    [Fact]
    public void OutsidePhoto_UsesExistingTenMeterStepAndSafetyMargin()
    {
        var plan = PlaceRadiusExpansionPlanner.Create(
            CenterLatitude,
            CenterLongitude,
            100,
            [Photo("outside.jpg", CenterLatitude + 0.0018, CenterLongitude)]);

        Assert.True(plan.NeedsExpansion);
        Assert.True(plan.ProposedRadiusMeters >= plan.RequiredRadiusMeters + 9.9);
        Assert.Equal(0, plan.ProposedRadiusMeters % PlaceRadiusExpansionPlanner.RadiusStepMeters);
        Assert.True(Assert.Single(plan.PhotoPoints).IsOutsideCurrentRadius);
    }

    [Fact]
    public void GpsLessPhotos_AreExcludedWhileGpsPhotosStillDetermineRadius()
    {
        var gpsId = Guid.NewGuid();
        var plan = PlaceRadiusExpansionPlanner.Create(
            CenterLatitude,
            CenterLongitude,
            100,
            [
                new PlaceRadiusPhotoSelection(Guid.NewGuid(), "no-gps.jpg", null, null),
                new PlaceRadiusPhotoSelection(gpsId, "gps.jpg", CenterLatitude + 0.0018, CenterLongitude),
            ]);

        var point = Assert.Single(plan.PhotoPoints);
        Assert.Equal(gpsId, point.MediaId);
        Assert.True(plan.NeedsExpansion);
    }

    [Fact]
    public void AllGpsLessPhotos_DoNotRequestPreviewOrRadiusPatch()
    {
        var plan = PlaceRadiusExpansionPlanner.Create(
            CenterLatitude,
            CenterLongitude,
            100,
            [new PlaceRadiusPhotoSelection(Guid.NewGuid(), "no-gps.jpg", null, null)]);

        Assert.False(plan.HasGpsPhotos);
        Assert.False(plan.NeedsExpansion);
        Assert.Empty(plan.PhotoPoints);
    }

    [Fact]
    public void AbnormallyDistantPhoto_ExceedsMaximumWithoutClamping()
    {
        var plan = PlaceRadiusExpansionPlanner.Create(
            CenterLatitude,
            CenterLongitude,
            100,
            [Photo("far-away.jpg", CenterLatitude + 0.08, CenterLongitude)]);

        Assert.True(plan.NeedsExpansion);
        Assert.True(plan.ExceedsMaximum);
        Assert.True(plan.ProposedRadiusMeters > PlaceRadiusExpansionPlanner.MaximumRadiusMeters);
    }

    private static PlaceRadiusPhotoSelection Photo(string name, double latitude, double longitude) =>
        new(Guid.NewGuid(), name, latitude, longitude);
}
