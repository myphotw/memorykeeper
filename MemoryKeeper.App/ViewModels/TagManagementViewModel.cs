using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MemoryKeeper.Application.DTOs;
using MemoryKeeper.Application.Services;
using MemoryKeeper.Infrastructure.Services.Api;
using Microsoft.Extensions.Logging;

namespace MemoryKeeper.App.ViewModels;

public partial class TagManagementViewModel : ObservableObject
{
    private readonly MemoryKeeperWriteService _writeService;
    private readonly ILogger<TagManagementViewModel> _logger;
    private IReadOnlyList<MemoryKeeperTagCatalogItemDto> _catalog = [];

    [ObservableProperty]
    private ObservableCollection<MemoryKeeperTagCatalogItemDto> tags = [];

    [ObservableProperty]
    private MemoryKeeperTagCatalogItemDto? selectedTag;

    [ObservableProperty]
    private string searchText = string.Empty;

    [ObservableProperty]
    private string name = string.Empty;

    [ObservableProperty]
    private string statusMessage = "정리할 태그를 선택하세요.";

    [ObservableProperty]
    private bool isBusy;

    public event EventHandler? BackRequested;

    public TagManagementViewModel(
        MemoryKeeperWriteService writeService,
        ILogger<TagManagementViewModel> logger)
    {
        _writeService = writeService;
        _logger = logger;
    }

    [RelayCommand]
    private void GoBack() => BackRequested?.Invoke(this, EventArgs.Empty);

    partial void OnSelectedTagChanged(MemoryKeeperTagCatalogItemDto? value) =>
        Name = value?.DisplayName ?? string.Empty;

    partial void OnSearchTextChanged(string value) => ApplySearch();

    [RelayCommand]
    private async Task LoadAsync()
    {
        await RunBusyAsync(async () =>
        {
            await LoadCoreAsync(SelectedTag?.Identity);
            StatusMessage = _catalog.Count == 0
                ? "등록된 태그가 없습니다."
                : $"태그 {_catalog.Count}개를 불러왔습니다.";
        });
    }

    [RelayCommand]
    private async Task SaveNameAsync()
    {
        if (SelectedTag is null)
        {
            StatusMessage = "이름을 변경할 태그를 선택하세요.";
            return;
        }

        var selected = SelectedTag;
        await RunBusyAsync(async () =>
        {
            var result = await _writeService.RenameCatalogTagAsync(
                selected.Identity,
                selected.Revision,
                Name);
            await LoadCoreAsync(result.Identity);
            StatusMessage = $"태그 이름을 '{result.DisplayName}'(으)로 변경했습니다.";
        });
    }

    [RelayCommand]
    private async Task DeleteAsync()
    {
        if (SelectedTag is null)
        {
            StatusMessage = "삭제할 태그를 선택하세요.";
            return;
        }

        var selected = SelectedTag;
        await RunBusyAsync(async () =>
        {
            await _writeService.DeleteCatalogTagAsync(selected.Identity, selected.Revision);
            await LoadCoreAsync();
            StatusMessage = $"태그 '{selected.DisplayName}'을(를) 삭제했습니다. 사진은 유지됩니다.";
        });
    }

    public MemoryKeeperTagCatalogItemDto? FindExistingName(string value)
    {
        var normalized = Normalize(value);
        return _catalog.FirstOrDefault(item =>
            !string.Equals(item.Identity, SelectedTag?.Identity, StringComparison.Ordinal)
            && string.Equals(Normalize(item.DisplayName), normalized, StringComparison.OrdinalIgnoreCase));
    }

    private async Task LoadCoreAsync(string? preferredIdentity = null)
    {
        var response = await _writeService.GetTagCatalogAsync();
        _catalog = response.Items;
        ApplySearch(preferredIdentity);
    }

    private void ApplySearch(string? preferredIdentity = null)
    {
        var currentIdentity = preferredIdentity ?? SelectedTag?.Identity;
        var term = Normalize(SearchText);
        var filtered = string.IsNullOrWhiteSpace(term)
            ? _catalog
            : _catalog.Where(item => item.DisplayName.Contains(term, StringComparison.OrdinalIgnoreCase)).ToList();
        Tags = new ObservableCollection<MemoryKeeperTagCatalogItemDto>(filtered);
        SelectedTag = Tags.FirstOrDefault(item =>
                          string.Equals(item.Identity, currentIdentity, StringComparison.Ordinal))
                      ?? Tags.FirstOrDefault();
    }

    private async Task RunBusyAsync(Func<Task> action)
    {
        if (IsBusy)
        {
            return;
        }

        try
        {
            IsBusy = true;
            await action();
        }
        catch (ApiException ex) when (ex.StatusCode == System.Net.HttpStatusCode.Conflict)
        {
            _logger.LogWarning(ex, "Tag catalog revision conflict.");
            await LoadCoreAsync();
            StatusMessage = "다른 곳에서 태그 정보가 변경되었습니다. 최신 정보를 다시 불러왔습니다.";
        }
        catch (ApiException ex)
        {
            _logger.LogWarning(ex, "Tag catalog operation failed.");
            StatusMessage = ApiErrorClassifier.ToUserMessage(ex, "요청한 태그를 찾을 수 없습니다.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Tag catalog operation failed.");
            StatusMessage = ex is ArgumentException ? ex.Message : "태그를 처리하지 못했습니다. 잠시 후 다시 시도해 주세요.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private static string Normalize(string? value) =>
        string.Join(" ", (value ?? string.Empty).Split(
            ' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
}
