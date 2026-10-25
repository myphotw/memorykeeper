using MemoryKeeper.Application;
using MemoryKeeper.Application.DTOs;

namespace MemoryKeeper.Tests.UnitTests;

public sealed class PlaceManagementExperienceTests
{
    [Fact]
    public void HierarchyProjection_KeepsSameUuidCanonicalPathsAndDifferentUuidNamesDistinct()
    {
        var sharedId = Guid.NewGuid();
        var otherId = Guid.NewGuid();
        var places = new[]
        {
            Place(sharedId, "헐후메일 퍼블릭 비치"),
            Place(otherId, "헐후메일 퍼블릭 비치"),
        };
        var paths = new[]
        {
            HierarchyPath(sharedId, "male-path", "몰디브", "말레", 11),
            HierarchyPath(sharedId, "lot-path", "몰디브", "Lot", 1),
            HierarchyPath(otherId, "busan-path", "대한민국", "부산", 2),
        };

        var result = PlacePickerHierarchyProjection.Build(places, paths, includeInactive: true);

        Assert.Equal(2, result.AllPathItems.Count(item => item.Id == sharedId));
        Assert.All(result.AllPathItems.Where(item => item.Id == sharedId), item => Assert.True(item.HasMultipleHierarchyPaths));
        Assert.Single(result.AllPathItems.Where(item => item.Id == otherId));
    }

    [Fact]
    public void PlaceManagement_UsesLinkedPhotosAndRadiusImpactAsDifferentCounts()
    {
        var xaml = ReadSource("MemoryKeeper.App", "Views", "PlaceManagementView.xaml");
        var viewModel = ReadSource("MemoryKeeper.App", "ViewModels", "PlaceManagementViewModel.cs");

        Assert.Contains("PlaceHierarchy", xaml, StringComparison.Ordinal);
        Assert.Contains("LinkedPhotoCountText", xaml, StringComparison.Ordinal);
        Assert.Contains("IncludedPhotoCountText", xaml, StringComparison.Ordinal);
        Assert.Contains("GetLinkedPhotosAsync", viewModel, StringComparison.Ordinal);
        Assert.Contains("GetRadiusImpactAsync", viewModel, StringComparison.Ordinal);
        Assert.Contains("실제 연결 사진", viewModel, StringComparison.Ordinal);
        Assert.Contains("반경 내 사진", viewModel, StringComparison.Ordinal);
    }

    [Fact]
    public void PlaceManagement_MergeDeleteAndReverseGeocodeKeepSafetyContracts()
    {
        var viewModel = ReadSource("MemoryKeeper.App", "ViewModels", "PlaceManagementViewModel.cs");
        var service = ReadSource("MemoryKeeper.Application", "Services", "MemoryKeeperPlaceService.cs");

        Assert.Contains("GetMergePreviewAsync", viewModel, StringComparison.Ordinal);
        Assert.Contains("var sourceRevision = preview.SourceRevision > 0", viewModel, StringComparison.Ordinal);
        Assert.Contains("var targetRevision = preview.TargetRevision > 0", viewModel, StringComparison.Ordinal);
        Assert.Contains("ExpectedSourceRevision = sourceRevision", viewModel, StringComparison.Ordinal);
        Assert.Contains("ExpectedTargetRevision = targetRevision", viewModel, StringComparison.Ordinal);
        Assert.Contains("실제 연결 사진", viewModel, StringComparison.Ordinal);
        Assert.Contains("삭제할 수 없습니다. 다른 장소와 병합하세요", viewModel, StringComparison.Ordinal);
        Assert.Contains("DisplayName = originalName", viewModel, StringComparison.Ordinal);
        Assert.Contains("LatitudeText = originalLatitude", viewModel, StringComparison.Ordinal);
        Assert.Contains("RadiusText = originalRadius", viewModel, StringComparison.Ordinal);
        Assert.DoesNotContain("미등록 상태로 전환", service, StringComparison.Ordinal);
    }

    private static PlaceDto Place(Guid id, string name) => new()
    {
        Id = id,
        DisplayName = name,
        IsActive = true,
    };

    private static MemoryKeeperPlaceHierarchyPathApiDto HierarchyPath(
        Guid id,
        string key,
        string country,
        string region,
        int count) => new()
    {
        MemorykeeperPlaceId = id,
        LocationKey = key,
        Country = country,
        Region = region,
        DisplayName = "헐후메일 퍼블릭 비치",
        FullPath = $"{country} > {region} > 헐후메일 퍼블릭 비치",
        PhotoCount = count,
        LocationHierarchyComplete = true,
    };

    private static string ReadSource(params string[] parts)
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
