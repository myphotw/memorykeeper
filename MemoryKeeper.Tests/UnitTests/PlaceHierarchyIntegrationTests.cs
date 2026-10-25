using MemoryKeeper.Application;
using MemoryKeeper.Application.DTOs;
using System.Text.Json;

namespace MemoryKeeper.Tests.UnitTests;

public sealed class PlaceHierarchyIntegrationTests
{
    [Theory]
    [InlineData("[{\"memorykeeper_place_id\":\"11111111-2222-3333-4444-555555555555\",\"country\":\"몰디브\",\"region\":\"말레\",\"location_path\":[\"몰디브\",\"말레\"],\"photo_count\":11}]")]
    [InlineData("{\"items\":[{\"memorykeeper_place_id\":\"11111111-2222-3333-4444-555555555555\",\"country\":\"몰디브\",\"region\":\"말레\",\"location_path\":[\"몰디브\",\"말레\"],\"photo_count\":11}]}")]
    public void HierarchyDto_DeserializesFlatPathsWithoutBuildingASecondClientHierarchy(string json)
    {
        var response = JsonSerializer.Deserialize<MemoryKeeperPlaceHierarchyListApiDto>(json)!;

        var path = Assert.Single(response.Items);
        Assert.Equal(Guid.Parse("11111111-2222-3333-4444-555555555555"), path.MemorykeeperPlaceId);
        Assert.Equal("몰디브", path.Country);
        Assert.Equal("말레", path.Region);
        Assert.Equal(new[] { "몰디브", "말레" }, path.LocationPath);
        Assert.Equal(11, path.PhotoCount);
    }

    [Fact]
    public void Projection_UsesCanonicalCountryRegionAndKeepsSameUuidPathsDistinct()
    {
        var sharedId = Guid.NewGuid();
        var otherId = Guid.NewGuid();
        var islandId = Guid.NewGuid();
        var zeroPhotoId = Guid.NewGuid();
        var places = new[]
        {
            Place(sharedId, "헐후메일 퍼블릭 비치"),
            Place(otherId, "헐후메일 퍼블릭 비치"),
            Place(islandId, "Ozen Reserve Bolifushi"),
            Place(zeroPhotoId, "사진 없는 장소"),
        };
        var paths = new[]
        {
            HierarchyPath(sharedId, "몰디브", "말레", "헐후메일 퍼블릭 비치", 11, "male-key"),
            HierarchyPath(sharedId, "몰디브", "Lot", "헐후메일 퍼블릭 비치", 1, "lot-key"),
            HierarchyPath(otherId, "대한민국", "부산", "헐후메일 퍼블릭 비치", 2, "busan-key"),
            HierarchyPath(islandId, "몰디브", "Island", "Ozen Reserve Bolifushi", 0, "island-key"),
            HierarchyPath(zeroPhotoId, "일본", "교토", "사진 없는 장소", 0, "empty-key"),
        };

        var result = PlacePickerHierarchyProjection.Build(places, paths);

        var samePlacePaths = result.AllPathItems.Where(item => item.Id == sharedId).ToList();
        Assert.Equal(2, samePlacePaths.Count);
        Assert.All(samePlacePaths, item => Assert.True(item.HasMultipleHierarchyPaths));
        Assert.Equal(new[] { 1, 11 }, samePlacePaths.Select(item => item.PhotoCount).Order().ToArray());
        Assert.Contains(samePlacePaths, item => item.FullPath == "몰디브 > 말레 > 헐후메일 퍼블릭 비치");
        Assert.Contains(samePlacePaths, item => item.FullPath == "몰디브 > Lot > 헐후메일 퍼블릭 비치");

        var sameNameOtherIdentity = Assert.Single(result.AllPathItems.Where(item => item.Id == otherId));
        Assert.Equal("대한민국", sameNameOtherIdentity.Country);
        Assert.NotEqual(sharedId, sameNameOtherIdentity.Id);

        var island = Assert.Single(result.AllPathItems.Where(item => item.Id == islandId));
        Assert.Equal("Island", island.Region);
        Assert.Equal(0, island.PhotoCount);
        Assert.DoesNotContain("기타", island.FullPath, StringComparison.Ordinal);

        Assert.Contains(result.AllPathItems, item => item.Id == zeroPhotoId && item.PhotoCount == 0);
        Assert.All(samePlacePaths, item => Assert.Contains("같은 등록 장소", item.TreeDisplayText, StringComparison.Ordinal));
    }

    [Fact]
    public void Projection_KeepsActiveZeroPhotoPlaceSelectableWhenOlderHierarchyOmitsIt()
    {
        var id = Guid.NewGuid();

        var result = PlacePickerHierarchyProjection.Build([Place(id, "새 장소")], []);

        var fallback = Assert.Single(result.AllPathItems);
        Assert.Equal(id, fallback.Id);
        Assert.Equal(0, fallback.PhotoCount);
        Assert.False(fallback.LocationHierarchyComplete);
        Assert.Equal("REGION_MISSING", fallback.LocationCleanupReason);
    }

    [Fact]
    public void Projection_SearchesFullCanonicalPathWithoutParsingLocationKey()
    {
        var id = Guid.NewGuid();
        var place = Place(id, "해변");
        var result = PlacePickerHierarchyProjection.Build(
            [place],
            [HierarchyPath(id, "몰디브", "말레", "해변", 4, "opaque/location/key")]);

        var item = Assert.Single(result.AllPathItems);
        Assert.True(PlacePickerHierarchyProjection.Matches(item, place, "말레"));
        Assert.False(PlacePickerHierarchyProjection.Matches(item, place, "opaque/location/key"));
    }

    [Theory]
    [InlineData("PLACE_UNASSIGNED", "등록된 장소가 없습니다")]
    [InlineData("COUNTRY_MISSING", "국가 정보가 부족합니다")]
    [InlineData("REGION_MISSING", "지역 정보가 부족합니다")]
    [InlineData("PLACE_MISSING", "장소 이름이 부족합니다")]
    public void LocationWarning_MapsBackendReasonWithoutConflatingLegacyIncomplete(
        string reason,
        string expected)
    {
        var warning = LocationHierarchyDisplay.GetWarning(false, reason);
        Assert.NotNull(warning);
        Assert.Contains(expected, warning, StringComparison.Ordinal);
        Assert.Null(LocationHierarchyDisplay.GetWarning(true, reason));
        Assert.Null(LocationHierarchyDisplay.GetWarning(null, reason));
    }

    [Fact]
    public void RegisteredPlaceGeographyChange_RequiresImpactWarningButFavoriteOnlyDoesNot()
    {
        var original = new PlaceDto
        {
            Id = Guid.NewGuid(),
            DisplayName = "해변",
            IsActive = true,
            Country = "몰디브",
            Province = "Kaafu",
            City = "말레",
            MediaCount = 12,
        };
        var changed = Update(original, country: "몰디브", province: "Kaafu", city: "Lot");
        var unchanged = Update(original, country: " 몰디브 ", province: "Kaafu", city: "말레");

        Assert.True(PlaceHierarchyChangePolicy.HasCanonicalGeographyChanged(original, changed));
        Assert.False(PlaceHierarchyChangePolicy.HasCanonicalGeographyChanged(original, unchanged));
        Assert.True(PlaceHierarchyChangePolicy.HasCanonicalGeographyChanged(
            original,
            Update(original, country: "몰디브", province: "Kaafu", city: "말레", displayName: "새 해변 이름")));
        Assert.Contains("사진 12장", PlaceHierarchyChangePolicy.BuildImpactMessage(original.DisplayName, original.MediaCount));
        Assert.Contains("GPS·EXIF·raw metadata는 변경되지 않습니다", PlaceHierarchyChangePolicy.BuildImpactMessage(original.DisplayName, original.MediaCount));
    }

    [Fact]
    public void PcPlacePickers_UseCommonBackendHierarchyAndPreserveDirectAssignmentContract()
    {
        var gallery = LoadSource("MemoryKeeper.App", "ViewModels", "GalleryPlaceEditSessionViewModel.cs");
        var detail = LoadSource("MemoryKeeper.App", "ViewModels", "PhotoDetailViewModel.cs");
        var pending = LoadSource("MemoryKeeper.App", "ViewModels", "PendingMemoryViewModel.cs");
        var dialog = LoadSource("MemoryKeeper.App", "Dialogs", "PlaceRegistrationDialog.cs");
        var placeManagement = LoadSource("MemoryKeeper.App", "ViewModels", "PlaceManagementViewModel.cs");
        var galleryPage = LoadSource("MemoryKeeper.App", "Views", "GalleryPage.xaml.cs");

        Assert.Contains("GetPlaceHierarchyAsync", gallery, StringComparison.Ordinal);
        Assert.Contains("GetPlaceHierarchyAsync", detail, StringComparison.Ordinal);
        Assert.Contains("GetPlaceHierarchyAsync", pending, StringComparison.Ordinal);
        Assert.Contains("PlacePickerHierarchyProjection.Build", gallery, StringComparison.Ordinal);
        Assert.Contains("PlacePickerHierarchyProjection.Build", detail, StringComparison.Ordinal);
        Assert.Contains("PlacePickerHierarchyProjection.Build", pending, StringComparison.Ordinal);
        Assert.Contains("place.TreeDisplayText", dialog, StringComparison.Ordinal);
        Assert.Contains("같은 등록 장소", dialog, StringComparison.Ordinal);
        Assert.Contains("ConfirmHierarchyImpactAsync", placeManagement, StringComparison.Ordinal);
        Assert.Contains("PlaceHierarchyChangePolicy.HasCanonicalGeographyChanged", placeManagement, StringComparison.Ordinal);
        Assert.Contains("AssignPlaceDirectlyAsync", galleryPage, StringComparison.Ordinal);
    }

    private static PlaceDto Place(Guid id, string name) => new()
    {
        Id = id,
        DisplayName = name,
        IsActive = true,
    };

    private static MemoryKeeperPlaceHierarchyPathApiDto HierarchyPath(
        Guid id,
        string country,
        string region,
        string name,
        int count,
        string locationKey) => new()
    {
        MemorykeeperPlaceId = id,
        Country = country,
        Region = region,
        DisplayName = name,
        LocationKey = locationKey,
        LocationPath = [country, region],
        FullPath = $"{country} > {region} > {name}",
        PhotoCount = count,
        LocationHierarchyComplete = true,
    };

    private static UpdatePlaceRequest Update(
        PlaceDto original,
        string country,
        string province,
        string city,
        string? displayName = null) => new()
    {
        Id = original.Id,
        DisplayName = displayName ?? original.DisplayName,
        Country = country,
        Province = province,
        City = city,
        Latitude = original.Latitude,
        Longitude = original.Longitude,
        Radius = original.Radius,
    };

    private static string LoadSource(params string[] parts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine([directory.FullName, .. parts]);
            if (File.Exists(candidate))
            {
                return File.ReadAllText(candidate);
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException($"Source file was not found: {Path.Combine(parts)}");
    }
}
