using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MemoryKeeper.App.Models;
using MemoryKeeper.App.Services;
using Microsoft.Extensions.Logging;

namespace MemoryKeeper.App.ViewModels;

public partial class TravelCountryListViewModel : ObservableObject
{
    private readonly ITravelRecordsNavigationState _navigationState;
    private readonly ILogger<TravelCountryListViewModel> _logger;

    [ObservableProperty]
    private ObservableCollection<TravelForeignCountryItem> countries = [];

    [ObservableProperty]
    private bool isBusy;

    [ObservableProperty]
    private string statusMessage = "해외 방문 국가를 불러오는 중…";

    public event EventHandler<string>? OpenCountryGalleryRequested;
    public event EventHandler? BackRequested;

    public TravelCountryListViewModel(
        ITravelRecordsNavigationState navigationState,
        ILogger<TravelCountryListViewModel> logger)
    {
        _navigationState = navigationState;
        _logger = logger;
    }

    [RelayCommand]
    private async Task LoadAsync()
    {
        IsBusy = true;
        try
        {
            Countries = new ObservableCollection<TravelForeignCountryItem>(
                _navigationState.ForeignCountries.Select(country => new TravelForeignCountryItem(country)));
            StatusMessage = Countries.Count == 0
                ? "표시할 해외 방문 국가가 없습니다."
                : "국가를 선택하면 해당 사진을 사진첩에서 볼 수 있어요.";

            foreach (var country in Countries.Where(item => item.HasThumbnail))
            {
                country.ThumbnailImage = await HttpImageLoader.LoadAsync(
                    country.ThumbnailPath,
                    _logger,
                    context: $"TravelCountry:{country.Country}");
            }
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void OpenCountryGallery(TravelForeignCountryItem? item)
    {
        if (item is null || string.IsNullOrWhiteSpace(item.Country))
        {
            return;
        }

        OpenCountryGalleryRequested?.Invoke(this, item.Country);
    }

    [RelayCommand]
    private void GoBack() => BackRequested?.Invoke(this, EventArgs.Empty);
}
