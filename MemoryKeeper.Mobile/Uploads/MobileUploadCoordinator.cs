using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace MemoryKeeper.Mobile.Uploads;

public sealed class MobileUploadCoordinator : INotifyPropertyChanged
{
    public const int MaximumSelectionCount = 100;
    public const int MaximumConcurrentUploads = 2;

    private readonly IMobileMediaPicker _picker;
    private readonly IMobileContentHasher _hasher;
    private readonly IMobileUploadApiClient _apiClient;
    private readonly IMobileUploadRecoveryStore _recoveryStore;
    private readonly IMobileUploadScheduler _scheduler;
    private readonly SemaphoreSlim _uploadSlots = new(MaximumConcurrentUploads, MaximumConcurrentUploads);
    private readonly SemaphoreSlim _recoveryGate = new(1, 1);
    private readonly object _cancellationLock = new();
    private readonly Dictionary<MobileUploadItem, CancellationTokenSource> _itemCancellations = [];
    private string? _noticeMessage;
    private int _restoreStarted;

    public MobileUploadCoordinator(
        IMobileMediaPicker picker,
        IMobileContentHasher hasher,
        IMobileUploadApiClient apiClient,
        IMobileUploadRecoveryStore recoveryStore,
        IMobileUploadScheduler scheduler)
    {
        _picker = picker ?? throw new ArgumentNullException(nameof(picker));
        _hasher = hasher ?? throw new ArgumentNullException(nameof(hasher));
        _apiClient = apiClient ?? throw new ArgumentNullException(nameof(apiClient));
        _recoveryStore = recoveryStore ?? throw new ArgumentNullException(nameof(recoveryStore));
        _scheduler = scheduler ?? throw new ArgumentNullException(nameof(scheduler));
        Items.CollectionChanged += OnItemsChanged;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public event EventHandler? GalleryRefreshRequested;

    public ObservableCollection<MobileUploadItem> Items { get; } = [];

    public bool HasItems => Items.Count > 0;

    public int ActiveCount => Items.Count(item => item.CanCancel);

    public bool IsBusy => ActiveCount > 0;

    public int CompletedCount => Items.Count(item => item.State == MobileUploadItemState.Completed);

    public int DuplicateCount => Items.Count(item => item.State == MobileUploadItemState.Duplicate);

    public int FailedCount => Items.Count(item => item.State is MobileUploadItemState.Failed
        or MobileUploadItemState.TrackingStopped);

    public string SummaryText => Items.Count == 0
        ? "선택한 파일이 없습니다."
        : $"전체 {Items.Count} · 진행 {ActiveCount} · 완료 {CompletedCount} · 중복 {DuplicateCount} · 확인 필요 {FailedCount}";

    public string CompactSummaryText => IsBusy
        ? $"업로드 {ActiveCount}개 진행 중"
        : $"업로드 결과 {Items.Count}개";

    public string? NoticeMessage
    {
        get => _noticeMessage;
        private set
        {
            if (string.Equals(_noticeMessage, value, StringComparison.Ordinal))
            {
                return;
            }

            _noticeMessage = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsNoticeVisible));
        }
    }

    public bool IsNoticeVisible => !string.IsNullOrWhiteSpace(NoticeMessage);

    public async Task SelectAndQueueAsync(CancellationToken cancellationToken = default)
    {
        IReadOnlyList<MobileSelectedMediaFile> selected;
        try
        {
            selected = await _picker.PickMultipleAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return;
        }
        catch (MobileUploadException exception)
        {
            NoticeMessage = ToUserMessage(exception.Code);
            return;
        }
        catch
        {
            NoticeMessage = "파일을 선택할 수 없습니다. 잠시 후 다시 시도해 주세요.";
            return;
        }

        if (selected.Count == 0)
        {
            return;
        }

        if (selected.Count > MaximumSelectionCount)
        {
            NoticeMessage = $"한 번에 최대 {MaximumSelectionCount}개까지 추가할 수 있어 처음 {MaximumSelectionCount}개만 처리합니다.";
            await ReleaseSourcesSafelyAsync(selected.Skip(MaximumSelectionCount));
            selected = selected.Take(MaximumSelectionCount).ToArray();
        }
        else
        {
            NoticeMessage = null;
        }

        _ = RunBatchSafelyAsync(selected);
    }

    public async Task QueueSelectedAsync(
        IReadOnlyList<MobileSelectedMediaFile> selected,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(selected);
        var accepted = selected.Take(MaximumSelectionCount).ToArray();
        await ReleaseSourcesSafelyAsync(selected.Skip(MaximumSelectionCount));
        if (accepted.Length == 0)
        {
            return;
        }

        var items = accepted.Select(source => new MobileUploadItem(source)).ToArray();
        foreach (var item in items)
        {
            Subscribe(item);
            Items.Insert(0, item);
        }

        MobileUploadBatchCapabilities capabilities;
        try
        {
            capabilities = await _apiClient.GetCapabilitiesAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            foreach (var item in items)
            {
                item.SetState(MobileUploadItemState.Canceled, "사용자가 업로드를 취소했습니다.");
            }

            return;
        }
        catch (MobileUploadException exception)
        {
            FailAll(items, exception.Code);
            return;
        }
        catch
        {
            FailAll(items, MobileUploadErrorCodes.Network);
            return;
        }

        if (!capabilities.CanUpload)
        {
            FailAll(items, capabilities.ResultCode ?? MobileUploadErrorCodes.ServerUpdateRequired);
            return;
        }

        await Task.WhenAll(items.Select(item =>
            ProcessItemAndNotifyAsync(item, capabilities, cancellationToken)));
    }

    public void Cancel(MobileUploadItem? item)
    {
        if (item is null)
        {
            return;
        }

        lock (_cancellationLock)
        {
            if (_itemCancellations.TryGetValue(item, out var cancellation))
            {
                cancellation.Cancel();
                return;
            }
        }

        // An item can still be waiting for the batch capability check, before its
        // per-item token has been registered. Preserve that cancellation locally.
        if (item.State == MobileUploadItemState.Queued)
        {
            item.SetState(MobileUploadItemState.Canceled, "사용자가 업로드를 취소했습니다.");
        }
    }

    public async Task RetryAsync(
        MobileUploadItem? item,
        CancellationToken cancellationToken = default)
    {
        if (item is null || !item.CanRetry)
        {
            return;
        }

        if (item.JobId is Guid acceptedJobId)
        {
            var completed = await ResumeAcceptedJobAsync(item, acceptedJobId, cancellationToken);
            if (completed)
            {
                GalleryRefreshRequested?.Invoke(this, EventArgs.Empty);
            }

            return;
        }

        MobileUploadBatchCapabilities capabilities;
        try
        {
            capabilities = await _apiClient.GetCapabilitiesAsync(cancellationToken);
        }
        catch (MobileUploadException exception)
        {
            item.SetState(MobileUploadItemState.Failed, ToUserMessage(exception.Code), exception.Code);
            return;
        }
        catch
        {
            item.SetState(
                MobileUploadItemState.Failed,
                ToUserMessage(MobileUploadErrorCodes.Network),
                MobileUploadErrorCodes.Network);
            return;
        }

        if (!capabilities.CanUpload)
        {
            var code = capabilities.ResultCode ?? MobileUploadErrorCodes.ServerUpdateRequired;
            item.SetState(MobileUploadItemState.Failed, ToUserMessage(code), code);
            return;
        }

        item.ResetForRetry();
        await ProcessItemAndNotifyAsync(item, capabilities, cancellationToken);
    }

    public async Task RestoreAsync(CancellationToken cancellationToken = default)
    {
        if (Interlocked.Exchange(ref _restoreStarted, 1) != 0)
        {
            return;
        }

        IReadOnlyList<MobileUploadRecoveryRecord> records;
        try
        {
            records = await _recoveryStore.LoadAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return;
        }
        catch
        {
            NoticeMessage = "이전 업로드 상태를 불러오지 못했습니다.";
            return;
        }

        var restored = new List<(MobileUploadItem Item, Guid JobId)>();
        foreach (var record in records)
        {
            if (Items.Any(item => item.JobId == record.JobId))
            {
                continue;
            }

            var item = MobileUploadItem.FromRecovery(record);
            Subscribe(item);
            Items.Add(item);
            restored.Add((item, record.JobId));
        }

        await Task.WhenAll(restored.Select(entry =>
            ResumeAcceptedJobAndNotifyAsync(entry.Item, entry.JobId, cancellationToken)));
    }

    private async Task RunBatchSafelyAsync(IReadOnlyList<MobileSelectedMediaFile> selected)
    {
        try
        {
            await QueueSelectedAsync(selected);
        }
        catch
        {
            NoticeMessage = "업로드 작업을 계속할 수 없습니다. 잠시 후 다시 시도해 주세요.";
        }
    }

    private async Task ProcessItemAndNotifyAsync(
        MobileUploadItem item,
        MobileUploadBatchCapabilities capabilities,
        CancellationToken cancellationToken)
    {
        if (await ProcessItemAsync(item, capabilities, cancellationToken))
        {
            GalleryRefreshRequested?.Invoke(this, EventArgs.Empty);
        }
    }

    private async Task ResumeAcceptedJobAndNotifyAsync(
        MobileUploadItem item,
        Guid jobId,
        CancellationToken cancellationToken)
    {
        if (await ResumeAcceptedJobAsync(item, jobId, cancellationToken))
        {
            GalleryRefreshRequested?.Invoke(this, EventArgs.Empty);
        }
    }

    private async Task<bool> ProcessItemAsync(
        MobileUploadItem item,
        MobileUploadBatchCapabilities capabilities,
        CancellationToken outerCancellation)
    {
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(outerCancellation);
        if (!RegisterCancellation(item, cancellation))
        {
            return false;
        }

        var acquiredSlot = false;
        var proceedToTracking = false;
        try
        {
            if (item.State == MobileUploadItemState.Canceled)
            {
                return false;
            }

            await _uploadSlots.WaitAsync(cancellation.Token);
            acquiredSlot = true;
            var uploadFile = item.UploadFile
                             ?? throw new InvalidOperationException("The selected source is unavailable.");
            if (uploadFile.ClientContentSha256 is null)
            {
                item.SetState(MobileUploadItemState.Hashing);
                var hash = await Task.Run(
                    () => _hasher.ComputeSha256Async(uploadFile.Source, cancellation.Token),
                    cancellation.Token);
                uploadFile.SetContentSha256(hash.Sha256);
            }

            item.SetState(MobileUploadItemState.CheckingDuplicate);
            var preflight = await _apiClient.PreflightAsync(
                capabilities,
                uploadFile.ClientContentSha256!,
                cancellation.Token);
            if (preflight.Disposition == MobileUploadPreflightDisposition.Duplicate)
            {
                item.SetState(
                    MobileUploadItemState.Duplicate,
                    "이미 MemoryKeeper에 보관된 파일입니다.",
                    MobileUploadErrorCodes.DuplicateContent);
                await ReleaseSourceSafelyAsync(item);
                return false;
            }

            if (!preflight.ShouldUpload)
            {
                var code = preflight.FailureCode ?? MobileUploadErrorCodes.ServerUpdateRequired;
                item.SetState(MobileUploadItemState.Failed, ToUserMessage(code), code);
                return false;
            }

            item.SetState(MobileUploadItemState.Uploading);
            var progress = new Progress<MobileUploadProgress>(value =>
            {
                if (item.State == MobileUploadItemState.Uploading)
                {
                    item.SetTransferProgress(value.BytesSent);
                }
            });
            var response = await _apiClient.UploadAsync(uploadFile, cancellation.Token, progress);
            if (!Guid.TryParse(response.JobId, out var jobId))
            {
                throw new MobileUploadException(
                    MobileUploadErrorCodes.InvalidResponse,
                    "Backend returned an invalid job identifier.");
            }

            item.SetAcceptedJob(jobId, _scheduler.UtcNow);
            // Hashing and upload both used the same private staged file. Once the
            // Backend has accepted a Job, retries only poll that Job and no longer
            // need the local original bytes.
            await ReleaseSourceSafelyAsync(item);
            // Once the server has accepted a Job, persist it even if the user stops local tracking.
            try
            {
                await SaveAcceptedJobAsync(item, CancellationToken.None);
            }
            catch
            {
                NoticeMessage = "이 업로드의 재시작 복구 정보를 저장하지 못했습니다. 앱을 종료하지 말고 상태를 확인해 주세요.";
            }

            item.SetState(MobileUploadItemState.ServerProcessing);
            proceedToTracking = true;
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            SetCanceledState(item);
            return false;
        }
        catch (MobileUploadException exception)
        {
            ApplyUploadFailure(item, exception.Code);
            if (IsTerminalSourceFailure(exception.Code))
            {
                await ReleaseSourceSafelyAsync(item);
            }

            return false;
        }
        catch
        {
            ApplyUploadFailure(item, MobileUploadErrorCodes.Network);
            return false;
        }
        finally
        {
            if (acquiredSlot)
            {
                _uploadSlots.Release();
            }

            if (!proceedToTracking)
            {
                UnregisterCancellation(item, cancellation);
            }
        }

        try
        {
            return await TrackAcceptedJobAsync(item, item.JobId!.Value, cancellation.Token);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            SetCanceledState(item);
            return false;
        }
        finally
        {
            UnregisterCancellation(item, cancellation);
        }
    }

    private async Task<bool> ResumeAcceptedJobAsync(
        MobileUploadItem item,
        Guid jobId,
        CancellationToken outerCancellation)
    {
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(outerCancellation);
        if (!RegisterCancellation(item, cancellation))
        {
            return false;
        }

        item.SetState(MobileUploadItemState.ServerProcessing);
        try
        {
            return await TrackAcceptedJobAsync(item, jobId, cancellation.Token);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            SetCanceledState(item);
            return false;
        }
        finally
        {
            UnregisterCancellation(item, cancellation);
        }
    }

    private async Task<bool> TrackAcceptedJobAsync(
        MobileUploadItem item,
        Guid jobId,
        CancellationToken cancellationToken)
    {
        while (true)
        {
            MobileUploadJobResult result;
            try
            {
                result = await _apiClient.GetJobAsync(jobId, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (MobileUploadException exception)
            {
                item.SetState(
                    MobileUploadItemState.TrackingStopped,
                    ToUserMessage(exception.Code),
                    exception.Code);
                return false;
            }
            catch
            {
                item.SetState(
                    MobileUploadItemState.TrackingStopped,
                    ToUserMessage(MobileUploadErrorCodes.Network),
                    MobileUploadErrorCodes.Network);
                return false;
            }

            item.SetServerProgress(result.Status.Progress);
            if (result.Status.IsCompleted)
            {
                item.SetState(MobileUploadItemState.Completed, "Gallery에 반영되었습니다.");
                await RemoveAcceptedJobSafelyAsync(jobId);
                return true;
            }

            if (result.Status.IsFailed)
            {
                if (result.FailureKind == MobileUploadJobFailureKind.DuplicateContent)
                {
                    item.SetState(
                        MobileUploadItemState.Duplicate,
                        "이미 MemoryKeeper에 보관된 파일입니다.",
                        MobileUploadErrorCodes.DuplicateContent);
                }
                else
                {
                    var code = result.FailureKind == MobileUploadJobFailureKind.ContentSha256Mismatch
                        ? MobileUploadErrorCodes.ContentSha256Mismatch
                        : result.Status.FailureCode ?? MobileUploadErrorCodes.Server;
                    item.SetState(MobileUploadItemState.Failed, ToUserMessage(code), code);
                }

                await RemoveAcceptedJobSafelyAsync(jobId);
                return false;
            }

            item.SetState(MobileUploadItemState.ServerProcessing);
            await _scheduler.DelayBeforeNextJobPollAsync(cancellationToken);
        }
    }

    private bool RegisterCancellation(
        MobileUploadItem item,
        CancellationTokenSource cancellation)
    {
        lock (_cancellationLock)
        {
            if (_itemCancellations.ContainsKey(item))
            {
                return false;
            }

            _itemCancellations[item] = cancellation;
            return true;
        }
    }

    private void UnregisterCancellation(
        MobileUploadItem item,
        CancellationTokenSource cancellation)
    {
        lock (_cancellationLock)
        {
            if (_itemCancellations.TryGetValue(item, out var current)
                && ReferenceEquals(current, cancellation))
            {
                _itemCancellations.Remove(item);
            }
        }
    }

    private async Task SaveAcceptedJobAsync(
        MobileUploadItem item,
        CancellationToken cancellationToken)
    {
        if (item.JobId is not Guid jobId || item.AcceptedAt is not DateTimeOffset acceptedAt)
        {
            return;
        }

        await _recoveryGate.WaitAsync(cancellationToken);
        try
        {
            var records = (await _recoveryStore.LoadAsync(cancellationToken)).ToList();
            records.RemoveAll(record => record.JobId == jobId);
            records.Add(new MobileUploadRecoveryRecord(
                jobId,
                item.ClientFileId,
                item.FileName,
                acceptedAt));
            await _recoveryStore.SaveAsync(records, cancellationToken);
        }
        finally
        {
            _recoveryGate.Release();
        }
    }

    private async Task RemoveAcceptedJobAsync(Guid jobId, CancellationToken cancellationToken)
    {
        await _recoveryGate.WaitAsync(cancellationToken);
        try
        {
            var records = (await _recoveryStore.LoadAsync(cancellationToken))
                .Where(record => record.JobId != jobId)
                .ToArray();
            await _recoveryStore.SaveAsync(records, cancellationToken);
        }
        finally
        {
            _recoveryGate.Release();
        }
    }

    private async Task RemoveAcceptedJobSafelyAsync(Guid jobId)
    {
        try
        {
            await RemoveAcceptedJobAsync(jobId, CancellationToken.None);
        }
        catch
        {
            NoticeMessage = "완료된 업로드의 복구 정보를 정리하지 못했습니다. 다음 실행에서 다시 확인합니다.";
        }
    }

    private void SetCanceledState(MobileUploadItem item)
    {
        if (item.JobId.HasValue)
        {
            item.SetState(
                MobileUploadItemState.TrackingStopped,
                "서버 작업은 취소하지 않고 이 기기에서 상태 확인만 중단했습니다.");
        }
        else
        {
            item.SetState(MobileUploadItemState.Canceled, "사용자가 업로드를 취소했습니다.");
        }
    }

    private static void ApplyUploadFailure(MobileUploadItem item, string code)
    {
        if (string.Equals(code, MobileUploadErrorCodes.DuplicateContent, StringComparison.Ordinal))
        {
            item.SetState(
                MobileUploadItemState.Duplicate,
                "이미 MemoryKeeper에 보관된 파일입니다.",
                code);
            return;
        }

        item.SetState(MobileUploadItemState.Failed, ToUserMessage(code), code);
    }

    private static void FailAll(IEnumerable<MobileUploadItem> items, string code)
    {
        foreach (var item in items)
        {
            if (item.State != MobileUploadItemState.Canceled)
            {
                item.SetState(MobileUploadItemState.Failed, ToUserMessage(code), code);
            }
        }
    }

    private static string ToUserMessage(string? code) => code switch
    {
        MobileUploadErrorCodes.DuplicateContent => "이미 MemoryKeeper에 보관된 파일입니다.",
        MobileUploadErrorCodes.ContentSha256Mismatch => "파일 확인 결과가 일치하지 않습니다. 다시 선택해 주세요.",
        MobileUploadErrorCodes.AuthenticationRequired => "서버 인증을 확인할 수 없습니다.",
        MobileUploadErrorCodes.ServerUpdateRequired => "모바일 업로드를 사용하려면 서버 업데이트가 필요합니다.",
        MobileUploadErrorCodes.FileAccess => "파일 접근 권한이 만료되었습니다. 파일을 다시 선택해 주세요.",
        MobileUploadErrorCodes.OriginalMediaPermissionRequired =>
            "사진의 GPS를 포함한 원본을 보관하려면 사진 위치정보 접근을 허용한 뒤 다시 선택해 주세요.",
        MobileUploadErrorCodes.OriginalMediaUnavailable =>
            "GPS를 포함한 사진 원본을 읽을 수 없습니다. 기기에 원본을 저장한 뒤 다시 선택해 주세요.",
        MobileUploadErrorCodes.Network => "네트워크에 연결할 수 없습니다. 연결 후 다시 시도해 주세요.",
        MobileUploadErrorCodes.Conflict => "같은 업로드 요청의 상태를 확인할 수 없습니다. 잠시 후 다시 시도해 주세요.",
        _ => "서버에서 파일을 처리하지 못했습니다. 잠시 후 다시 시도해 주세요.",
    };

    private static bool IsTerminalSourceFailure(string code) =>
        code is MobileUploadErrorCodes.DuplicateContent
            or MobileUploadErrorCodes.ContentSha256Mismatch
            or MobileUploadErrorCodes.OriginalMediaPermissionRequired
            or MobileUploadErrorCodes.OriginalMediaUnavailable;

    private static Task ReleaseSourcesSafelyAsync(
        IEnumerable<MobileSelectedMediaFile> sources) =>
        Task.WhenAll(sources.Select(async source =>
        {
            try
            {
                await source.ReleaseAsync();
            }
            catch
            {
            }
        }));

    private static async Task ReleaseSourceSafelyAsync(MobileUploadItem item)
    {
        if (item.UploadFile is null)
        {
            return;
        }

        try
        {
            await item.UploadFile.Source.ReleaseAsync();
        }
        catch
        {
        }
    }

    private void Subscribe(MobileUploadItem item) => item.PropertyChanged += OnItemPropertyChanged;

    private void OnItemsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        NotifySummaryChanged();
    }

    private void OnItemPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        NotifySummaryChanged();
    }

    private void NotifySummaryChanged()
    {
        OnPropertyChanged(nameof(HasItems));
        OnPropertyChanged(nameof(ActiveCount));
        OnPropertyChanged(nameof(IsBusy));
        OnPropertyChanged(nameof(CompletedCount));
        OnPropertyChanged(nameof(DuplicateCount));
        OnPropertyChanged(nameof(FailedCount));
        OnPropertyChanged(nameof(SummaryText));
        OnPropertyChanged(nameof(CompactSummaryText));
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
