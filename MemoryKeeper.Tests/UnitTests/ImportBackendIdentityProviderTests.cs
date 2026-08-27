using MemoryKeeper.Application.DTOs;
using MemoryKeeper.Application.DTOs.Gallery;
using MemoryKeeper.Application.DTOs.Upload;
using MemoryKeeper.Application.Interfaces;
using MemoryKeeper.Application.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace MemoryKeeper.Tests.UnitTests;

public sealed class ImportBackendIdentityProviderTests
{
    [Fact]
    public async Task Snapshot_UsesPagedBatchListsInsteadOfPerFileRequests()
    {
        var gallery = new PagedGallery(450);
        var jobs = new PagedJobs(450);
        var sessionHash = 9999.ToString("x64");
        var session = new SessionStore
        {
            Jobs =
            [
                new ImportSessionJobDto
                {
                    JobId = Guid.NewGuid().ToString("D"),
                    FileName = "session.jpg",
                    LocalFilePath = "source/session.jpg",
                    Status = "Waiting",
                    ContentHash = sessionHash,
                },
            ],
        };
        var provider = new ImportBackendIdentityProvider(
            gallery,
            jobs,
            session,
            NullLogger<ImportBackendIdentityProvider>.Instance);

        var snapshot = await provider.LoadAsync();

        Assert.True(snapshot.IsComplete);
        Assert.Equal(450, snapshot.ExistingContentHashes.Count);
        Assert.Contains(sessionHash, snapshot.AcceptedContentHashes);
        Assert.Equal(3, gallery.CallCount);
        Assert.Equal(9, jobs.CallCount);
        Assert.All(gallery.PageSizes, size => Assert.Equal(200, size));
        Assert.All(jobs.PageSizes, size => Assert.Equal(200, size));
    }

    private sealed class PagedGallery(int total) : IGalleryApiRepository
    {
        public int CallCount { get; private set; }
        public List<int> PageSizes { get; } = [];

        public Task<PagedResult<PhotoDto>> GetPhotosAsync(int page = 1, int pageSize = 20, string sort = "capture_datetime_desc", string? serviceName = null, CancellationToken cancellationToken = default)
        {
            CallCount++;
            PageSizes.Add(pageSize);
            var items = Enumerable.Range((page - 1) * pageSize, Math.Max(0, Math.Min(pageSize, total - ((page - 1) * pageSize))))
                .Select(index => new PhotoDto { FileId = (index + 1).ToString("x64"), Filename = $"{index}.jpg" })
                .ToList();
            return Task.FromResult(new PagedResult<PhotoDto> { Items = items, TotalCount = total, Page = page, PageSize = pageSize });
        }

        public Task<MemoryKeeper.Application.DTOs.Gallery.PhotoDetailDto> GetPhotoAsync(Guid fileId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<PagedResult<PhotoDto>> SearchAsync(int? year = null, string? country = null, string? city = null, string? camera = null, string? tag = null, bool? favorite = null, string? serviceName = null, DateTimeOffset? dateFrom = null, DateTimeOffset? dateTo = null, string? keyword = null, int page = 1, int pageSize = 20, string sort = "capture_datetime_desc", string? province = null, string? district = null, string? place = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<MapResultDto> GetMapAsync(int? year = null, string? serviceName = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<TimelineResultDto> GetTimelineAsync(string? serviceName = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<StatisticsDto> GetStatisticsAsync(string? serviceName = null, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class PagedJobs(int total) : IUploadJobApiRepository
    {
        public int CallCount { get; private set; }
        public List<int> PageSizes { get; } = [];

        public Task<UploadJobStatusDto> GetStatusAsync(Guid jobId, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<UploadJobListDto> ListJobsAsync(string? status = null, int page = 1, int pageSize = 20, string sort = "created_at_desc", CancellationToken cancellationToken = default)
        {
            CallCount++;
            PageSizes.Add(pageSize);
            var items = Enumerable.Range((page - 1) * pageSize, Math.Max(0, Math.Min(pageSize, total - ((page - 1) * pageSize))))
                .Select(index => new UploadJobStatusDto
                {
                    JobId = Guid.NewGuid().ToString("D"),
                    Status = UploadJobStatusDto.Waiting,
                    ServiceName = "MemoryKeeper",
                    ClientFileId = (index + 1000).ToString("x64"),
                })
                .ToList();
            return Task.FromResult(new UploadJobListDto { Items = items, Total = total, Page = page, PageSize = pageSize });
        }
    }

    private sealed class SessionStore : IImportJobSessionStore
    {
        public List<ImportSessionJobDto> Jobs { get; init; } = [];
        public Task SaveAsync(IReadOnlyList<ImportSessionJobDto> jobs, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task UpdateAsync(IReadOnlyList<ImportSessionJobDto> openJobs, IReadOnlyCollection<string> managedJobIds, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<IReadOnlyList<ImportSessionJobDto>> LoadAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<ImportSessionJobDto>>(Jobs);
        public Task ClearAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
