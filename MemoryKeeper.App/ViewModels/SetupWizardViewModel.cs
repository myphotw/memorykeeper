using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MemoryKeeper.App.Services;
using MemoryKeeper.Application.Services;
using Microsoft.Extensions.Logging;

namespace MemoryKeeper.App.ViewModels;

public partial class SetupWizardViewModel : ObservableObject
{
    private readonly SetupWizardService _setupWizardService;
    private readonly IFolderPickerService _folderPickerService;
    private readonly ILogger<SetupWizardViewModel> _logger;

    [ObservableProperty]
    private int currentStep = 1;

    [ObservableProperty]
    private string storageName = "Photo Library";

    [ObservableProperty]
    private string photoRoot = string.Empty;

    [ObservableProperty]
    private string homeAddress = string.Empty;

    [ObservableProperty]
    private string homeLatitude = string.Empty;

    [ObservableProperty]
    private string homeLongitude = string.Empty;

    [ObservableProperty]
    private string googleMapsApiKey = string.Empty;

    [ObservableProperty]
    private string statusMessage = "MemoryKeeper 저장소 폴더를 선택하세요.";

    [ObservableProperty]
    private bool isBusy;

    [ObservableProperty]
    private bool canFinish;

    public event EventHandler? SetupCompleted;

    public SetupWizardViewModel(
        SetupWizardService setupWizardService,
        IFolderPickerService folderPickerService,
        ILogger<SetupWizardViewModel> logger)
    {
        _setupWizardService = setupWizardService;
        _folderPickerService = folderPickerService;
        _logger = logger;
    }

    [RelayCommand]
    private async Task BrowsePhotoRootAsync()
    {
        var path = await _folderPickerService.PickFolderAsync("MemoryKeeper 저장소 폴더 선택");
        if (!string.IsNullOrWhiteSpace(path))
        {
            Directory.CreateDirectory(path);
            PhotoRoot = path;
        }
    }

    [RelayCommand]
    private async Task NextAsync()
    {
        if (IsBusy)
        {
            return;
        }

        try
        {
            IsBusy = true;
            switch (CurrentStep)
            {
                case 1:
                    if (string.IsNullOrWhiteSpace(PhotoRoot))
                    {
                        StatusMessage = "MemoryKeeper 저장소 폴더를 선택하세요.";
                        return;
                    }

                    var status = await _setupWizardService.GetStatusAsync();
                    if (!status.HasStorage)
                    {
                        await _setupWizardService.CreateInitialStorageAsync(StorageName, PhotoRoot);
                    }

                    StatusMessage = "Home Location을 설정하세요. (주소 또는 좌표)";
                    CurrentStep = 2;
                    break;

                case 2:
                    await SaveHomeAsync();
                    StatusMessage = "Google Maps API Key는 선택 사항입니다. 없으면 지도 기능만 비활성화됩니다.";
                    CurrentStep = 3;
                    break;

                case 3:
                    await _setupWizardService.SaveGoogleMapsApiKeyAsync(GoogleMapsApiKey);
                    StatusMessage = "설정을 완료하면 Home으로 이동합니다.";
                    CurrentStep = 4;
                    CanFinish = true;
                    break;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Setup wizard step {Step} failed.", CurrentStep);
            StatusMessage = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void Back()
    {
        if (CurrentStep <= 1 || IsBusy)
        {
            return;
        }

        CurrentStep--;
        CanFinish = false;
        StatusMessage = CurrentStep switch
        {
            1 => "MemoryKeeper 저장소 폴더를 선택하세요.",
            2 => "Home Location을 설정하세요.",
            3 => "Google Maps API Key (선택).",
            _ => StatusMessage
        };
    }

    [RelayCommand]
    private Task SkipMapsAsync()
    {
        if (CurrentStep != 3 || IsBusy)
        {
            return Task.CompletedTask;
        }

        // Do not clear an existing API Key when skipping (MK-042I retention rule).
        StatusMessage = "지도 API Key 설정을 건너뛰었습니다. 기존 Key가 있으면 유지됩니다.";
        CurrentStep = 4;
        CanFinish = true;
        return Task.CompletedTask;
    }

    [RelayCommand]
    private async Task FinishAsync()
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        try
        {
            await _setupWizardService.MarkSetupCompletedAsync();
            SetupCompleted?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to mark setup complete.");
            StatusMessage = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task SaveHomeAsync()
    {
        if (!string.IsNullOrWhiteSpace(HomeAddress))
        {
            try
            {
                await _setupWizardService.SaveHomeByAddressAsync(HomeAddress);
                return;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Address geocode failed; trying coordinates.");
                if (!TryParseCoordinates(out var lat, out var lon))
                {
                    throw new InvalidOperationException(
                        "주소 변환에 실패했습니다. Google Maps API Key를 나중에 설정하거나 위도/경도를 직접 입력하세요. " + ex.Message);
                }

                await _setupWizardService.SaveHomeByCoordinatesAsync(lat, lon, HomeAddress);
                return;
            }
        }

        if (TryParseCoordinates(out var latitude, out var longitude))
        {
            await _setupWizardService.SaveHomeByCoordinatesAsync(latitude, longitude);
            return;
        }

        throw new InvalidOperationException("Home 주소 또는 위도/경도를 입력하세요.");
    }

    private bool TryParseCoordinates(out double latitude, out double longitude)
    {
        latitude = 0;
        longitude = 0;
        return double.TryParse(HomeLatitude.Trim(), System.Globalization.NumberStyles.Float,
                   System.Globalization.CultureInfo.InvariantCulture, out latitude)
               && double.TryParse(HomeLongitude.Trim(), System.Globalization.NumberStyles.Float,
                   System.Globalization.CultureInfo.InvariantCulture, out longitude);
    }
}
