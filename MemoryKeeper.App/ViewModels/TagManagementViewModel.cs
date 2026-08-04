using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MemoryKeeper.Application.DTOs;
using MemoryKeeper.Application.Services;
using Microsoft.Extensions.Logging;

namespace MemoryKeeper.App.ViewModels;

public partial class TagManagementViewModel : ObservableObject
{
    private readonly TagService _tagService;
    private readonly ILogger<TagManagementViewModel> _logger;

    [ObservableProperty]
    private ObservableCollection<TagDto> tags = [];

    [ObservableProperty]
    private TagDto? selectedTag;

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
        TagService tagService,
        ILogger<TagManagementViewModel> logger)
    {
        _tagService = tagService;
        _logger = logger;
    }

    [RelayCommand]
    private void GoBack() => BackRequested?.Invoke(this, EventArgs.Empty);

    partial void OnSelectedTagChanged(TagDto? value)
    {
        Name = value?.Name ?? string.Empty;
        IsPinned = value?.IsPinned ?? false;
    }

    [RelayCommand]
    private async Task LoadAsync()
    {
        await RunBusyAsync(async () =>
        {
            var items = await _tagService.GetTagListAsync();
            Tags = new ObservableCollection<TagDto>(items);
            SelectedTag = Tags.FirstOrDefault(tag => tag.Id == SelectedTag?.Id)
                ?? Tags.FirstOrDefault();
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
            var created = await _tagService.CreateTagAsync(new CreateTagRequest { Name = Name });
            if (IsPinned)
            {
                created = await _tagService.SetPinnedAsync(new SetPinnedTagRequest
                {
                    TagId = created.Id,
                    IsPinned = true
                });
            }

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
            var renamed = await _tagService.RenameTagAsync(new RenameTagRequest
            {
                TagId = SelectedTag.Id,
                Name = Name
            });
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
            var updated = await _tagService.SetPinnedAsync(new SetPinnedTagRequest
            {
                TagId = SelectedTag.Id,
                IsPinned = IsPinned
            });
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
            await _tagService.DeleteTagAsync(SelectedTag.Id);
            StatusMessage = $"Tag '{name}'을(를) 삭제했습니다. 사진은 유지됩니다.";
            await LoadCoreAsync();
        });
    }

    private async Task LoadCoreAsync()
    {
        var items = await _tagService.GetTagListAsync();
        Tags = new ObservableCollection<TagDto>(items);
        SelectedTag = Tags.FirstOrDefault();
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
