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

    [ObservableProperty]
    private ObservableCollection<MemoryKeeperTagDto> tags = [];

    [ObservableProperty]
    private MemoryKeeperTagDto? selectedTag;

    [ObservableProperty]
    private MemoryKeeperTagDto? mergeTargetTag;

    [ObservableProperty]
    private string name = string.Empty;

    [ObservableProperty]
    private bool isPinned;

    [ObservableProperty]
    private string statusMessage = "Tag를 선택하거나 새로 등록하세요.";

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

    partial void OnSelectedTagChanged(MemoryKeeperTagDto? value)
    {
        Name = value?.Name ?? string.Empty;
        IsPinned = value?.IsPinned ?? false;
    }

    [RelayCommand]
    private async Task LoadAsync()
    {
        await RunBusyAsync(async () =>
        {
            var items = (await _writeService.GetTagsAsync()).Items;
            Tags = new ObservableCollection<MemoryKeeperTagDto>(items);
            SelectedTag = Tags.FirstOrDefault(tag => tag.Id == SelectedTag?.Id)
                ?? Tags.FirstOrDefault();
            MergeTargetTag = Tags.FirstOrDefault(tag => tag.Id != SelectedTag?.Id);
            StatusMessage = Tags.Count == 0
                ? "등록된 Tag가 없습니다."
                : $"Tag {Tags.Count}개 로드됨.";
        });
    }

    [RelayCommand]
    private void ClearForm()
    {
        SelectedTag = null;
        Name = string.Empty;
        IsPinned = false;
        StatusMessage = "새 Tag 입력 모드입니다.";
    }

    [RelayCommand]
    private async Task CreateAsync()
    {
        await RunBusyAsync(async () =>
        {
            var created = await _writeService.CreateTagAsync(Name, IsPinned);

            StatusMessage = $"Tag '{created.Name}'을(를) 생성했습니다.";
            await LoadCoreAsync();
            SelectedTag = Tags.FirstOrDefault(tag => tag.Id == created.Id);
        });
    }

    [RelayCommand]
    private async Task RenameAsync()
    {
        if (SelectedTag is null)
        {
            StatusMessage = "이름을 변경할 Tag를 선택하세요.";
            return;
        }

        await RunBusyAsync(async () =>
        {
            var renamed = await _writeService.UpdateTagAsync(
                SelectedTag.Id, SelectedTag.Revision, Name, favorite: null);
            StatusMessage = $"Tag 이름을 '{renamed.Name}'(으)로 변경했습니다.";
            await LoadCoreAsync();
            SelectedTag = Tags.FirstOrDefault(tag => tag.Id == renamed.Id);
        });
    }

    [RelayCommand]
    private async Task SavePinnedAsync()
    {
        if (SelectedTag is null)
        {
            StatusMessage = "고정할 Tag를 선택하세요.";
            return;
        }

        await RunBusyAsync(async () =>
        {
            var updated = await _writeService.UpdateTagAsync(
                SelectedTag.Id, SelectedTag.Revision, name: null, favorite: IsPinned);
            StatusMessage = updated.IsPinned
                ? $"Tag '{updated.Name}'을(를) 고정했습니다."
                : $"Tag '{updated.Name}' 고정을 해제했습니다.";
            await LoadCoreAsync();
            SelectedTag = Tags.FirstOrDefault(tag => tag.Id == updated.Id);
        });
    }

    [RelayCommand]
    private async Task DeleteAsync()
    {
        if (SelectedTag is null)
        {
            StatusMessage = "삭제할 Tag를 선택하세요.";
            return;
        }

        await RunBusyAsync(async () =>
        {
            var name = SelectedTag.Name;
            await _writeService.DeleteTagAsync(SelectedTag.Id, SelectedTag.Revision);
            StatusMessage = $"Tag '{name}'을(를) 삭제했습니다. 사진은 유지됩니다.";
            await LoadCoreAsync();
        });
    }

    [RelayCommand]
    private async Task MergeAsync()
    {
        if (SelectedTag is null || MergeTargetTag is null)
        {
            StatusMessage = "병합할 원본 태그와 대상 태그를 선택하세요.";
            return;
        }

        await RunBusyAsync(async () =>
        {
            var target = await _writeService.MergeTagAsync(SelectedTag, MergeTargetTag);
            StatusMessage = $"태그를 '{target.Name}'(으)로 병합했습니다.";
            await LoadCoreAsync();
            SelectedTag = Tags.FirstOrDefault(tag => tag.Id == target.Id);
        });
    }

    private async Task LoadCoreAsync()
    {
        var items = (await _writeService.GetTagsAsync()).Items;
        Tags = new ObservableCollection<MemoryKeeperTagDto>(items);
        SelectedTag = Tags.FirstOrDefault();
        MergeTargetTag = Tags.FirstOrDefault(tag => tag.Id != SelectedTag?.Id);
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
        catch (ApiException ex) when (
            ex.StatusCode == System.Net.HttpStatusCode.Conflict
            && string.Equals(ex.DetailCode, "DUPLICATE_TAG_NAME", StringComparison.Ordinal))
        {
            _logger.LogWarning(ex, "Duplicate MemoryKeeper tag name.");
            StatusMessage = "같은 이름의 태그가 이미 있습니다.";
        }
        catch (ApiException ex) when (ex.StatusCode == System.Net.HttpStatusCode.Conflict)
        {
            _logger.LogWarning(ex, "Tag revision conflict.");
            await LoadCoreAsync();
            StatusMessage = "다른 곳에서 태그가 변경되었습니다. 최신 목록을 다시 불러왔습니다.";
        }
        catch (ApiException ex)
        {
            _logger.LogWarning(ex, "Tag management API operation failed.");
            StatusMessage = ApiErrorClassifier.ToUserMessage(ex, "요청한 태그를 찾을 수 없습니다.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Tag management operation failed.");
            StatusMessage = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }
}
