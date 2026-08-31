namespace MemoryKeeper.Tests.UnitTests;

public sealed class TravelRecordsPageRegressionTests
{
    [Fact]
    public void PageOrdersSummaryCountryGraphAndMemoryCardsWithoutLegacyTimelineOrTopCountryCard()
    {
        var xaml = File.ReadAllText(FindSourceFile(
            "MemoryKeeper.App", "Views", "TravelRecordsPage.xaml"));

        var summary = xaml.IndexOf("<!-- Memory Insight -->", StringComparison.Ordinal);
        var country = xaml.IndexOf("<!-- Country visit graph:", StringComparison.Ordinal);
        var memories = xaml.IndexOf("<!-- MemoryKeeper-selected memories -->", StringComparison.Ordinal);

        Assert.True(summary >= 0 && country > summary && memories > country);
        Assert.Contains("Text=\"국가별 방문 횟수\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Text=\"총 여행\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Text=\"해외 방문 국가\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Text=\"방문 장소\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Text=\"총 사진\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Text=\"방문 국가\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("방문 도시·여행", xaml, StringComparison.Ordinal);
        Assert.Contains("ColumnDefinitions=\"*,*,*,*\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("x:Name=\"StatCities\"", xaml, StringComparison.Ordinal);
        Assert.Contains("ItemsSource=\"{Binding CountryVisitStatistics, Mode=OneWay}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("ItemsSource=\"{Binding MemoryCards, Mode=OneWay}\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("가장 많이 방문한 나라", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Text=\"여행 타임라인\"", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("ItemsSource=\"{Binding YearChapters, Mode=OneWay}\"", xaml, StringComparison.Ordinal);

        var codeBehind = File.ReadAllText(FindSourceFile(
            "MemoryKeeper.App", "Views", "TravelRecordsPage.xaml.cs"));
        Assert.Contains("ViewModel.DistinctPlaceCount", codeBehind, StringComparison.Ordinal);
        Assert.Contains("ViewModel.UniquePhotoCount", codeBehind, StringComparison.Ordinal);
        Assert.Contains("ViewModel.VisitedForeignCountryCount", codeBehind, StringComparison.Ordinal);
        Assert.DoesNotContain("Select(trip => trip.Country)", codeBehind, StringComparison.Ordinal);
        Assert.DoesNotContain("StatCities", codeBehind, StringComparison.Ordinal);
        Assert.DoesNotContain("Select(trip => trip.TripName)", codeBehind, StringComparison.Ordinal);
        Assert.DoesNotContain("trips.Sum(trip => Math.Max(1, trip.PlaceCount))", codeBehind, StringComparison.Ordinal);
        Assert.DoesNotContain("trips.Sum(trip => trip.PhotoCount)", codeBehind, StringComparison.Ordinal);
    }

    private static string FindSourceFile(params string[] parts)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine([directory.FullName, .. parts]);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException($"Source file was not found: {Path.Combine(parts)}");
    }
}
