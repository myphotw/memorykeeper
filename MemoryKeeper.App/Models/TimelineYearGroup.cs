using System.Collections.ObjectModel;

namespace MemoryKeeper.App.Models;

public sealed class TimelineYearGroup
{
    public TimelineYearGroup(int year, IEnumerable<TimelinePlaceItem> places)
    {
        Year = year;
        YearTitle = year <= 0 ? "년도 미상" : $"{year}년";
        Places = new ObservableCollection<TimelinePlaceItem>(places);
    }

    public int Year { get; }

    public string YearTitle { get; }

    public ObservableCollection<TimelinePlaceItem> Places { get; }

    public int PlaceCount => Places.Count;
}
