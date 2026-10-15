using System.Text.Json;
using Microsoft.Maui.Storage;

namespace MemoryKeeper.Mobile.Uploads;

public sealed class MauiMobileUploadRecoveryStore : IMobileUploadRecoveryStore
{
    private const string PreferenceKey = "MemoryKeeper.Mobile.PendingUploadJobs.v1";
    private const int MaximumRecords = 100;

    public Task<IReadOnlyList<MobileUploadRecoveryRecord>> LoadAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var json = Preferences.Default.Get(PreferenceKey, string.Empty);
        if (string.IsNullOrWhiteSpace(json))
        {
            return Task.FromResult<IReadOnlyList<MobileUploadRecoveryRecord>>([]);
        }

        try
        {
            var records = JsonSerializer.Deserialize<List<MobileUploadRecoveryRecord>>(json) ?? [];
            return Task.FromResult<IReadOnlyList<MobileUploadRecoveryRecord>>(records
                .Where(IsValid)
                .OrderByDescending(record => record.AcceptedAt)
                .Take(MaximumRecords)
                .ToArray());
        }
        catch (JsonException)
        {
            Preferences.Default.Remove(PreferenceKey);
            return Task.FromResult<IReadOnlyList<MobileUploadRecoveryRecord>>([]);
        }
    }

    public Task SaveAsync(
        IReadOnlyList<MobileUploadRecoveryRecord> records,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var retained = records
            .Where(IsValid)
            .GroupBy(record => record.JobId)
            .Select(group => group.OrderByDescending(record => record.AcceptedAt).First())
            .OrderByDescending(record => record.AcceptedAt)
            .Take(MaximumRecords)
            .ToArray();
        if (retained.Length == 0)
        {
            Preferences.Default.Remove(PreferenceKey);
        }
        else
        {
            Preferences.Default.Set(PreferenceKey, JsonSerializer.Serialize(retained));
        }

        return Task.CompletedTask;
    }

    private static bool IsValid(MobileUploadRecoveryRecord record) =>
        record.JobId != Guid.Empty
        && Guid.TryParse(record.ClientFileId, out _)
        && !string.IsNullOrWhiteSpace(record.FileName);
}
