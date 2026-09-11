using System.Net;
using System.Text;
using System.Text.Json;
using MemoryKeeper.Application.DTOs;
using MemoryKeeper.Application.Interfaces;
using MemoryKeeper.Application.Services;
using MemoryKeeper.Infrastructure;
using MemoryKeeper.Infrastructure.Repositories.Api;
using MemoryKeeper.Infrastructure.Services.Api;
using MemoryKeeper.Infrastructure.Services.Api.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace MemoryKeeper.Tests.UnitTests;

public sealed class MemoryKeeperWriteApiRepositoryTests
{
    private const string FileId = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

    [Fact]
    public async Task MetadataPatch_OnlySendsChangedFields_AndDeleteUsesMemoryKeeperEndpoint()
    {
        var patchKey = $"PATCH /api/memorykeeper/files/{FileId}/metadata";
        var deleteKey = $"DELETE /api/memorykeeper/files/{FileId}";
        var handler = new RecordingHandler
        {
            Responses =
            {
                [patchKey] = $"{{\"file_id\":\"{FileId}\",\"favorite\":true,\"memo\":null,\"revision\":4,\"gps_lat\":37.5,\"gps_lon\":127.0,\"country\":\"대한민국\",\"province\":null,\"city\":null,\"district\":null,\"place_name\":null,\"memorykeeper_place_id\":null,\"place_revision\":9}}",
                [deleteKey] = $"{{\"file_id\":\"{FileId}\",\"cleanup_status\":\"CLEANED\",\"physical_file_deleted\":true}}",
            },
        };
        using var provider = BuildProvider(handler);
        var repository = provider.GetRequiredService<IMemoryKeeperWriteApiRepository>();

        var patched = await repository.PatchMetadataAsync(FileId, new MemoryKeeperFileMetadataPatchRequest
        {
            ExpectedRevision = 3,
            Favorite = true,
            ChangedFields = new HashSet<string> { "favorite" },
        });
        var deleted = await repository.DeleteFileAsync(FileId);

        using var payload = JsonDocument.Parse(handler.Bodies[patchKey]);
        Assert.Equal(2, payload.RootElement.EnumerateObject().Count());
        Assert.Equal(3, payload.RootElement.GetProperty("expected_revision").GetInt32());
        Assert.True(payload.RootElement.GetProperty("favorite").GetBoolean());
        Assert.Equal(4, patched.Revision);
        Assert.Equal(9, patched.PlaceRevision);
        Assert.True(deleted.PhysicalFileDeleted);
    }

    [Fact]
    public async Task MetadataPatch_CanExplicitlyClearMemoAndRawLocation()
    {
        var key = $"PATCH /api/memorykeeper/files/{FileId}/metadata";
        var handler = new RecordingHandler
        {
            Responses =
            {
                [key] = $"{{\"file_id\":\"{FileId}\",\"favorite\":false,\"memo\":null,\"revision\":2,\"gps_lat\":null,\"gps_lon\":null,\"country\":null,\"province\":null,\"city\":null,\"district\":null,\"place_name\":null,\"memorykeeper_place_id\":null,\"place_revision\":7}}",
            },
        };
        using var provider = BuildProvider(handler);
        var repository = provider.GetRequiredService<IMemoryKeeperWriteApiRepository>();

        await repository.PatchMetadataAsync(FileId, new MemoryKeeperFileMetadataPatchRequest
        {
            ExpectedRevision = 1,
            Memo = null,
            GpsLat = null,
            GpsLon = null,
            ChangedFields = new HashSet<string> { "memo", "gps_lat", "gps_lon" },
        });

        using var payload = JsonDocument.Parse(handler.Bodies[key]);
        Assert.Equal(JsonValueKind.Null, payload.RootElement.GetProperty("memo").ValueKind);
        Assert.Equal(JsonValueKind.Null, payload.RootElement.GetProperty("gps_lat").ValueKind);
        Assert.False(payload.RootElement.TryGetProperty("country", out _));
    }

    [Fact]
    public async Task TagCrudMergeAndFileRelations_UseIntegerTagIdentityAndRevisions()
    {
        var handler = new RecordingHandler
        {
            Responses =
            {
                ["GET /api/memorykeeper/tags?limit=500&offset=0"] = "{\"items\":[{\"id\":11,\"name\":\"가족\",\"tag_type\":\"USER\",\"source\":\"USER\",\"favorite\":true,\"usage_count\":1,\"revision\":2}],\"total\":1}",
                ["POST /api/memorykeeper/tags"] = "{\"id\":12,\"name\":\"여행\",\"tag_type\":\"USER\",\"source\":\"USER\",\"favorite\":false,\"usage_count\":0,\"revision\":1}",
                ["PATCH /api/memorykeeper/tags/11"] = "{\"id\":11,\"name\":\"우리 가족\",\"tag_type\":\"USER\",\"source\":\"USER\",\"favorite\":true,\"usage_count\":1,\"revision\":3}",
                ["DELETE /api/memorykeeper/tags/11?expected_revision=3"] = string.Empty,
                ["POST /api/memorykeeper/tags/12/merge"] = "{\"id\":11,\"name\":\"우리 가족\",\"tag_type\":\"USER\",\"source\":\"USER\",\"favorite\":true,\"usage_count\":2,\"revision\":4}",
                [$"POST /api/memorykeeper/files/{FileId}/tags/11"] = $"{{\"file_id\":\"{FileId}\",\"tag_id\":11,\"assigned\":true,\"revision\":5}}",
                [$"DELETE /api/memorykeeper/files/{FileId}/tags/11?expected_revision=5"] = $"{{\"file_id\":\"{FileId}\",\"tag_id\":11,\"assigned\":false,\"revision\":6}}",
            },
            StatusCodes = { ["DELETE /api/memorykeeper/tags/11?expected_revision=3"] = HttpStatusCode.NoContent },
        };
        using var provider = BuildProvider(handler);
        var repository = provider.GetRequiredService<IMemoryKeeperWriteApiRepository>();

        Assert.Equal(11, Assert.Single((await repository.GetTagsAsync()).Items).Id);
        await repository.CreateTagAsync(new MemoryKeeperTagCreateRequest { Name = "여행" });
        await repository.UpdateTagAsync(11, new MemoryKeeperTagUpdateRequest { Revision = 2, Name = "우리 가족" });
        await repository.DeleteTagAsync(11, 3);
        await repository.MergeTagAsync(12, new MemoryKeeperTagMergeRequest { SourceRevision = 1, TargetTagId = 11, TargetRevision = 3 });
        Assert.Equal(5, (await repository.AssignFileTagAsync(FileId, 11, 4)).Revision);
        Assert.Equal(6, (await repository.RemoveFileTagAsync(FileId, 11, 5)).Revision);

        using var createPayload = JsonDocument.Parse(handler.Bodies["POST /api/memorykeeper/tags"]);
        Assert.Equal("여행", createPayload.RootElement.GetProperty("name").GetString());
        Assert.False(createPayload.RootElement.GetProperty("favorite").GetBoolean());

        using var updatePayload = JsonDocument.Parse(handler.Bodies["PATCH /api/memorykeeper/tags/11"]);
        Assert.Equal(2, updatePayload.RootElement.GetProperty("revision").GetInt32());
        Assert.Equal("우리 가족", updatePayload.RootElement.GetProperty("name").GetString());
        Assert.False(updatePayload.RootElement.TryGetProperty("favorite", out _));

        using var mergePayload = JsonDocument.Parse(handler.Bodies["POST /api/memorykeeper/tags/12/merge"]);
        Assert.Equal(1, mergePayload.RootElement.GetProperty("source_revision").GetInt32());
        Assert.Equal(11, mergePayload.RootElement.GetProperty("target_tag_id").GetInt32());
        Assert.Equal(3, mergePayload.RootElement.GetProperty("target_revision").GetInt32());

        using var assignPayload = JsonDocument.Parse(handler.Bodies[$"POST /api/memorykeeper/files/{FileId}/tags/11"]);
        Assert.Equal(4, assignPayload.RootElement.GetProperty("expected_revision").GetInt32());
        Assert.False(handler.Bodies.ContainsKey("DELETE /api/memorykeeper/tags/11?expected_revision=3"));
        Assert.False(handler.Bodies.ContainsKey($"DELETE /api/memorykeeper/files/{FileId}/tags/11?expected_revision=5"));
    }

    [Fact]
    public async Task UnifiedCatalog_RenameCanChangeIdentity_AndDeleteUsesReturnedRevision()
    {
        const string getKey = "GET /api/memorykeeper/tags/catalog?limit=500&offset=0";
        const string patchKey = "PATCH /api/memorykeeper/tags/catalog/ai%3Adog";
        const string deleteKey = "DELETE /api/memorykeeper/tags/catalog/tag%3A123?expected_revision=2";
        var handler = new RecordingHandler
        {
            Responses =
            {
                [getKey] = "{\"items\":[{\"identity\":\"ai:dog\",\"display_name\":\"강아지\",\"usage_count\":12,\"favorite\":false,\"revision\":1,\"editable\":true,\"canonical_references\":[\"dog\"]}],\"total\":1}",
                [patchKey] = "{\"identity\":\"tag:123\",\"display_name\":\"반려동물\",\"usage_count\":12,\"favorite\":false,\"revision\":2,\"editable\":true,\"canonical_references\":[\"dog\"]}",
                [deleteKey] = "{}",
            },
        };
        using var provider = BuildProvider(handler);
        var repository = provider.GetRequiredService<IMemoryKeeperWriteApiRepository>();

        var original = Assert.Single((await repository.GetTagCatalogAsync()).Items);
        Assert.Equal("강아지", original.DisplayName);
        Assert.Equal(12, original.UsageCount);
        Assert.Null(original.ManagedTagId);

        var renamed = await repository.RenameCatalogTagAsync(
            original.Identity,
            new MemoryKeeperTagCatalogRenameRequest { Name = "반려동물", Revision = original.Revision });
        Assert.Equal("tag:123", renamed.Identity);
        Assert.Equal(123, renamed.ManagedTagId);
        Assert.Equal(2, renamed.Revision);
        await repository.DeleteCatalogTagAsync(renamed.Identity, renamed.Revision);

        using var payload = JsonDocument.Parse(handler.Bodies[patchKey]);
        Assert.Equal("반려동물", payload.RootElement.GetProperty("name").GetString());
        Assert.Equal(1, payload.RootElement.GetProperty("revision").GetInt32());
    }

    [Fact]
    public async Task FileCatalogTagMutations_UseOpaqueIdentityAndMetadataRevision_WithoutGlobalDelete()
    {
        var restoreKey = $"POST /api/memorykeeper/files/{FileId}/tags/catalog/ai%3Adog";
        var hideKey = $"DELETE /api/memorykeeper/files/{FileId}/tags/catalog/tag%3A42?expected_revision=6";
        var handler = new RecordingHandler
        {
            Responses =
            {
                [restoreKey] = $"{{\"file_id\":\"{FileId}\",\"identity\":\"ai:dog\",\"hidden\":false,\"revision\":6}}",
                [hideKey] = $"{{\"file_id\":\"{FileId}\",\"identity\":\"tag:42\",\"hidden\":true,\"revision\":7}}",
            },
        };
        using var provider = BuildProvider(handler);
        var repository = provider.GetRequiredService<IMemoryKeeperWriteApiRepository>();

        var restored = await repository.RestoreFileCatalogTagAsync(FileId, "ai:dog", 5);
        var hidden = await repository.HideFileCatalogTagAsync(FileId, "tag:42", restored.Revision);

        Assert.False(restored.Hidden);
        Assert.Equal("ai:dog", restored.Identity);
        Assert.Equal(6, restored.Revision);
        Assert.True(hidden.Hidden);
        Assert.Equal("tag:42", hidden.Identity);
        Assert.Equal(7, hidden.Revision);
        using var payload = JsonDocument.Parse(handler.Bodies[restoreKey]);
        Assert.Equal(5, payload.RootElement.GetProperty("expected_revision").GetInt32());
        Assert.Contains(restoreKey, handler.Requests);
        Assert.Contains(hideKey, handler.Requests);
        Assert.DoesNotContain(
            handler.Requests,
            request => request.StartsWith("DELETE /api/memorykeeper/tags/catalog/", StringComparison.Ordinal));
    }

    [Fact]
    public async Task PendingListAndBatchAssignment_PreserveRawGeographyAndPlaceRevision()
    {
        var pendingKey = "GET /api/memorykeeper/pending?page=1&page_size=200&include_suggestions=true";
        var assignKey = "POST /api/memorykeeper/pending/assign-place";
        var placeId = Guid.Parse("11111111-2222-3333-4444-555555555555");
        var handler = new RecordingHandler
        {
            Responses =
            {
                [pendingKey] = $"{{\"items\":[{{\"file_id\":\"{FileId}\",\"thumbnail_url\":\"/thumb.jpg\",\"capture_datetime\":\"2026-08-15T01:00:00Z\",\"gps_lat\":37.5,\"gps_lon\":127.0,\"country\":\"대한민국\",\"province\":\"서울특별시\",\"city\":\"서울\",\"district\":\"종로구\",\"place_name\":\"원시 주소\",\"memorykeeper_place_id\":null,\"place_revision\":7}}],\"total\":1,\"page\":1,\"page_size\":200}}",
                [assignKey] = $"{{\"items\":[{{\"file_id\":\"{FileId}\",\"memorykeeper_place_id\":\"{placeId:D}\",\"place_revision\":8}}],\"assigned_count\":1}}",
            },
        };
        using var provider = BuildProvider(handler);
        var repository = provider.GetRequiredService<IMemoryKeeperWriteApiRepository>();

        var item = Assert.Single((await repository.GetPendingAsync()).Items);
        Assert.Equal("원시 주소", item.PlaceName);
        Assert.Equal(7, item.PlaceRevision);
        Assert.Equal("http://localhost:8000/thumb.jpg", item.ThumbnailUrl);
        await repository.AssignPendingPlaceAsync(new MemoryKeeperPendingAssignRequest
        {
            FileIds = [FileId],
            MemorykeeperPlaceId = placeId,
            ExpectedRevisions = new Dictionary<string, int> { [FileId] = 7 },
        });

        using var payload = JsonDocument.Parse(handler.Bodies[assignKey]);
        Assert.Equal(7, payload.RootElement.GetProperty("expected_revisions").GetProperty(FileId).GetInt32());
        Assert.Equal(placeId, payload.RootElement.GetProperty("memorykeeper_place_id").GetGuid());
    }

    [Fact]
    public async Task GalleryBatchPlaceApis_PreserveShaNumericIdentityNullsAndRevisionMap()
    {
        var secondFileId = new string('b', 64);
        var targetPlaceId = Guid.Parse("11111111-2222-3333-4444-555555555555");
        const string stateKey = "POST /api/memorykeeper/files/place-state/query";
        const string assignKey = "POST /api/memorykeeper/files/assign-place";
        var handler = new RecordingHandler
        {
            Responses =
            {
                [stateKey] = $"{{\"items\":[{{\"file_id\":\"{FileId}\",\"common_file_id\":9223372036,\"gps_lat\":null,\"gps_lon\":null,\"memorykeeper_place_id\":null,\"place_match_revision\":7}},{{\"file_id\":\"{secondFileId}\",\"common_file_id\":42,\"gps_lat\":26.2,\"gps_lon\":127.7,\"memorykeeper_place_id\":\"{targetPlaceId:D}\",\"place_match_revision\":9}}]}}",
                [assignKey] = $"{{\"items\":[{{\"file_id\":\"{FileId}\",\"memorykeeper_place_id\":\"{targetPlaceId:D}\",\"place_revision\":8}}],\"assigned_count\":2}}",
            },
        };
        using var provider = BuildProvider(handler);
        var repository = provider.GetRequiredService<IMemoryKeeperWriteApiRepository>();

        var state = await repository.QueryFilePlaceStatesAsync(new MemoryKeeperFilePlaceStateQueryRequest
        {
            FileIds = [FileId, secondFileId],
        });
        await repository.AssignFilePlacesAsync(new MemoryKeeperFilesAssignPlaceRequest
        {
            FileIds = [FileId, secondFileId],
            MemorykeeperPlaceId = targetPlaceId,
            ExpectedPlaceRevisions = new Dictionary<string, int>
            {
                [FileId] = 7,
                [secondFileId] = 9,
            },
        });

        Assert.Equal(FileId, state.Items[0].FileId);
        Assert.Equal(9223372036L, state.Items[0].CommonFileId);
        Assert.Null(state.Items[0].GpsLat);
        Assert.Null(state.Items[0].MemorykeeperPlaceId);
        Assert.Equal(targetPlaceId, state.Items[1].MemorykeeperPlaceId);
        using var statePayload = JsonDocument.Parse(handler.Bodies[stateKey]);
        Assert.Equal(FileId, statePayload.RootElement.GetProperty("file_ids")[0].GetString());
        using var assignPayload = JsonDocument.Parse(handler.Bodies[assignKey]);
        Assert.Equal(targetPlaceId, assignPayload.RootElement.GetProperty("memorykeeper_place_id").GetGuid());
        Assert.Equal(7, assignPayload.RootElement.GetProperty("expected_place_revisions").GetProperty(FileId).GetInt32());
        Assert.Equal(9, assignPayload.RootElement.GetProperty("expected_place_revisions").GetProperty(secondFileId).GetInt32());
    }

    [Fact]
    public async Task GalleryBatchWorkflow_RefreshesRevisionAssignsAtomicallyAndVerifiesInOneBatch()
    {
        var targetPlaceId = Guid.Parse("11111111-2222-3333-4444-555555555555");
        const string stateKey = "POST /api/memorykeeper/files/place-state/query";
        const string assignKey = "POST /api/memorykeeper/files/assign-place";
        var handler = new RecordingHandler
        {
            Responses =
            {
                [stateKey] = $"{{\"items\":[{{\"file_id\":\"{FileId}\",\"common_file_id\":42,\"gps_lat\":null,\"gps_lon\":null,\"memorykeeper_place_id\":\"{targetPlaceId:D}\",\"place_match_revision\":7}}]}}",
                [assignKey] = $"{{\"items\":[{{\"file_id\":\"{FileId}\",\"memorykeeper_place_id\":\"{targetPlaceId:D}\",\"place_revision\":7}}],\"assigned_count\":1}}",
            },
        };
        using var provider = BuildProvider(handler);
        var repository = provider.GetRequiredService<IMemoryKeeperWriteApiRepository>();
        var workflow = new MemoryKeeper.Application.Services.GalleryPlaceAssignmentWorkflow(
            repository,
            new CatalogInvalidation());
        var stages = new List<GalleryPlaceAssignmentStage>();
        var diagnostics = new List<GalleryPlaceAssignmentDiagnosticSnapshot>();

        var result = await workflow.AssignAsync(
            [FileId],
            targetPlaceId,
            reportStage: stages.Add,
            reportDiagnostic: diagnostics.Add);

        Assert.True(result.IsVerified);
        Assert.Equal(
            new[]
            {
                GalleryPlaceAssignmentStage.RevisionRefresh,
                GalleryPlaceAssignmentStage.Assign,
                GalleryPlaceAssignmentStage.Invalidate,
                GalleryPlaceAssignmentStage.VerifyQuery,
                GalleryPlaceAssignmentStage.VerifyMatch,
                GalleryPlaceAssignmentStage.Completed,
            },
            stages);
        var completedDiagnostic = Assert.Single(
            diagnostics.Where(item => item.Stage == GalleryPlaceAssignmentStage.Completed));
        Assert.Equal(1, completedDiagnostic.SelectedCount);
        Assert.Equal(1, completedDiagnostic.ReturnedCount);
        Assert.Equal(1, completedDiagnostic.RevisionMapCount);
        Assert.Equal(1, completedDiagnostic.VerifiedCount);
        Assert.Equal(0, completedDiagnostic.MismatchCount);
        Assert.Equal(2, handler.Requests.Count(request => request == stateKey));
        Assert.Single(handler.Requests.Where(request => request == assignKey));
        using var payload = JsonDocument.Parse(handler.Bodies[assignKey]);
        Assert.Equal(7, payload.RootElement.GetProperty("expected_place_revisions").GetProperty(FileId).GetInt32());
    }

    [Fact]
    public async Task GalleryBatchWorkflow_RejectsMoreThanBackendMaximumBeforeSending()
    {
        var handler = new RecordingHandler();
        using var provider = BuildProvider(handler);
        var workflow = new MemoryKeeper.Application.Services.GalleryPlaceAssignmentWorkflow(
            provider.GetRequiredService<IMemoryKeeperWriteApiRepository>(),
            new CatalogInvalidation());
        var fileIds = Enumerable.Range(0, 501).Select(index => index.ToString("x64")).ToList();

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => workflow.QueryStatesAsync(fileIds));

        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task PlaceCleanup_UsesPagedEndpointAndPreservesExistingPlaceIdentity()
    {
        const string cleanupKey = "GET /api/memorykeeper/place-cleanup?page=2&page_size=50";
        var existingPlaceId = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
        var handler = new RecordingHandler
        {
            Responses =
            {
                [cleanupKey] = $"{{\"items\":[{{\"file_id\":\"{FileId}\",\"thumbnail_url\":\"/cleanup-thumb.jpg\",\"memorykeeper_place_id\":\"{existingPlaceId:D}\",\"place_revision\":11}}],\"total\":1721,\"page\":2,\"page_size\":50}}",
            },
        };
        using var provider = BuildProvider(handler);
        var repository = provider.GetRequiredService<IMemoryKeeperWriteApiRepository>();

        var result = await repository.GetPlaceCleanupAsync(page: 2, pageSize: 50);

        var item = Assert.Single(result.Items);
        Assert.Equal(1721, result.Total);
        Assert.Equal(2, result.Page);
        Assert.Equal(50, result.PageSize);
        Assert.Equal(existingPlaceId, item.MemorykeeperPlaceId);
        Assert.Equal("http://localhost:8000/cleanup-thumb.jpg", item.ThumbnailUrl);
        Assert.Contains(cleanupKey, handler.Requests);
    }

    [Fact]
    public async Task CleanupGroups_UseIndependentOpaqueCursorsAndGroupPhotoEndpoints()
    {
        const string placeGroupsKey = "GET /api/memorykeeper/place-cleanup/groups?limit=5&cursor=place-next";
        const string dateGroupsKey = "GET /api/memorykeeper/capture-date-cleanup/groups?limit=5&cursor=date-next";
        const string placePhotosKey = "GET /api/memorykeeper/place-cleanup/groups/place%3Abucket%2F1/photos?limit=50&cursor=place-photo-next";
        const string datePhotosKey = "GET /api/memorykeeper/capture-date-cleanup/groups/date%3Afallback%2F1/photos?limit=50&cursor=date-photo-next";
        var handler = new RecordingHandler
        {
            Responses =
            {
                [placeGroupsKey] = "{\"items\":[{\"group_id\":\"place:bucket/1\",\"issue_type\":\"PLACE_UNASSIGNED\",\"title\":\"서울\",\"media_count\":7,\"representative_thumbnail_url\":\"/place.jpg\"}],\"next_cursor\":\"place-next-2\",\"has_more\":true,\"total_groups\":9,\"total_photos\":40}",
                [dateGroupsKey] = "{\"items\":[{\"group_id\":\"date:fallback/1\",\"title\":\"촬영일 확인\",\"media_count\":3,\"cleanup_reason\":\"FALLBACK_DATE_REQUIRES_REVIEW\",\"date_basis\":\"IMPORTED\"}],\"next_cursor\":\"date-next-2\",\"has_more\":true,\"total_groups\":8,\"total_photos\":30}",
                [placePhotosKey] = $"{{\"items\":[{{\"file_id\":\"{FileId}\",\"thumbnail_url\":\"/place-photo.jpg\",\"place_revision\":4}}],\"next_cursor\":null,\"has_more\":false,\"total_photos\":1}}",
                [datePhotosKey] = $"{{\"items\":[{{\"file_id\":\"{FileId}\",\"thumbnail_url\":\"/date-photo.jpg\",\"effective_capture_datetime\":\"2023-10-14T00:00:00Z\",\"user_capture_precision\":\"DATE\",\"date_basis\":\"USER\",\"date_revision\":5}}],\"next_cursor\":null,\"has_more\":false,\"total_photos\":1}}",
            },
        };
        using var provider = BuildProvider(handler);
        var repository = provider.GetRequiredService<IMemoryKeeperWriteApiRepository>();

        var place = await repository.GetPlaceCleanupGroupsAsync(5, "place-next");
        var date = await repository.GetCaptureDateCleanupGroupsAsync(5, "date-next");
        var placePhotos = await repository.GetPlaceCleanupGroupPhotosAsync("place:bucket/1", 50, "place-photo-next");
        var datePhotos = await repository.GetCaptureDateCleanupGroupPhotosAsync("date:fallback/1", 50, "date-photo-next");

        Assert.Equal("place-next-2", place.NextCursor);
        Assert.Equal("date-next-2", date.NextCursor);
        Assert.Equal("http://localhost:8000/place.jpg", Assert.Single(place.Items).RepresentativeThumbnailUrl);
        Assert.Equal("FALLBACK_DATE_REQUIRES_REVIEW", Assert.Single(date.Items).CleanupReason);
        Assert.Equal(4, Assert.Single(placePhotos.Items).PlaceRevision);
        Assert.Equal(5, Assert.Single(datePhotos.Items).DateRevision);
        Assert.Contains(placePhotosKey, handler.Requests);
        Assert.Contains(datePhotosKey, handler.Requests);
    }

    [Fact]
    public async Task CaptureDateMutation_SendsDateOnlyRevisionMapAndExplicitNullOverride()
    {
        const string key = "POST /api/memorykeeper/files/capture-date";
        var handler = new RecordingHandler
        {
            Responses =
            {
                [key] = $"{{\"items\":[{{\"file_id\":\"{FileId}\",\"user_capture_datetime\":\"2023-10-14T00:00:00Z\",\"user_capture_precision\":\"DATE\",\"effective_capture_date\":\"2023-10-14\",\"date_basis\":\"USER\",\"date_revision\":5}}],\"updated_count\":1}}",
            },
        };
        using var provider = BuildProvider(handler);
        var repository = provider.GetRequiredService<IMemoryKeeperWriteApiRepository>();

        var updated = await repository.SetCaptureDateAsync(new MemoryKeeperCaptureDateMutationRequest
        {
            FileIds = [FileId],
            UserCaptureDate = "2023-10-14",
            ExpectedDateRevisions = new Dictionary<string, int> { [FileId] = 4 },
        });
        using (var payload = JsonDocument.Parse(handler.Bodies[key]))
        {
            Assert.Equal("2023-10-14", payload.RootElement.GetProperty("user_capture_date").GetString());
            Assert.Equal(4, payload.RootElement.GetProperty("expected_date_revisions").GetProperty(FileId).GetInt32());
        }
        Assert.Equal("DATE", Assert.Single(updated.Items).UserCapturePrecision);

        await repository.SetCaptureDateAsync(new MemoryKeeperCaptureDateMutationRequest
        {
            FileIds = [FileId],
            UserCaptureDate = null,
            ExpectedDateRevisions = new Dictionary<string, int> { [FileId] = 5 },
        });
        using var clearPayload = JsonDocument.Parse(handler.Bodies[key]);
        Assert.Equal(JsonValueKind.Null, clearPayload.RootElement.GetProperty("user_capture_date").ValueKind);
    }

    [Fact]
    public async Task CaptureDateMutation_ExposesStructuredRevisionConflict()
    {
        const string key = "POST /api/memorykeeper/files/capture-date";
        var handler = new RecordingHandler
        {
            Responses =
            {
                [key] = $"{{\"detail\":{{\"code\":\"REVISION_CONFLICT\",\"files\":[{{\"file_id\":\"{FileId}\",\"expected_revision\":4,\"current_revision\":5}}]}}}}",
            },
            StatusCodes = { [key] = HttpStatusCode.Conflict },
        };
        using var provider = BuildProvider(handler);
        var repository = provider.GetRequiredService<IMemoryKeeperWriteApiRepository>();

        var error = await Assert.ThrowsAsync<ApiException>(() => repository.SetCaptureDateAsync(
            new MemoryKeeperCaptureDateMutationRequest
            {
                FileIds = [FileId],
                UserCaptureDate = "2023-10-14",
                ExpectedDateRevisions = new Dictionary<string, int> { [FileId] = 4 },
            }));

        Assert.Equal(HttpStatusCode.Conflict, error.StatusCode);
        Assert.Equal("REVISION_CONFLICT", error.DetailCode);
    }

    [Fact]
    public async Task Conflict_IsExposedToCallerForRefreshFlow()
    {
        var key = $"PATCH /api/memorykeeper/files/{FileId}/metadata";
        var handler = new RecordingHandler
        {
            Responses = { [key] = "{\"detail\":{\"current_revision\":5}}" },
            StatusCodes = { [key] = HttpStatusCode.Conflict },
        };
        using var provider = BuildProvider(handler);
        var repository = provider.GetRequiredService<IMemoryKeeperWriteApiRepository>();

        var error = await Assert.ThrowsAsync<ApiException>(() => repository.PatchMetadataAsync(
            FileId,
            new MemoryKeeperFileMetadataPatchRequest
            {
                ExpectedRevision = 4,
                Memo = "stale",
                ChangedFields = new HashSet<string> { "memo" },
            }));
        Assert.Equal(HttpStatusCode.Conflict, error.StatusCode);
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.Conflict)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    public async Task Delete_ExposesBackendStatusForFriendlyUx(HttpStatusCode statusCode)
    {
        var key = $"DELETE /api/memorykeeper/files/{FileId}";
        var handler = new RecordingHandler
        {
            Responses = { [key] = "{\"detail\":\"delete failed\"}" },
            StatusCodes = { [key] = statusCode },
        };
        using var provider = BuildProvider(handler);
        var repository = provider.GetRequiredService<IMemoryKeeperWriteApiRepository>();

        var error = await Assert.ThrowsAsync<ApiException>(() => repository.DeleteFileAsync(FileId));
        Assert.Equal(statusCode, error.StatusCode);
    }

    private static ServiceProvider BuildProvider(HttpMessageHandler handler)
    {
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.SetMinimumLevel(LogLevel.Warning));
        services.AddTcBackendApiClient(options =>
        {
            options.ApiBaseUrl = "http://localhost:8000";
            options.AuthToken = "write-test-token";
            options.Timeout = 10;
            options.RetryCount = 0;
        });
        services.PostConfigure<TcBackendOptions>(options =>
        {
            options.ApiBaseUrl = "http://localhost:8000";
            options.AuthToken = "write-test-token";
        });
        services.AddHttpClient(BaseApiClient.HttpClientName)
            .ConfigurePrimaryHttpMessageHandler(() => handler);
        services.AddSingleton<IMemoryKeeperWriteApiRepository, MemoryKeeperWriteApiRepository>();
        return services.BuildServiceProvider();
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        public Dictionary<string, string> Responses { get; init; } = new(StringComparer.Ordinal);
        public Dictionary<string, HttpStatusCode> StatusCodes { get; init; } = new(StringComparer.Ordinal);
        public Dictionary<string, string> Bodies { get; } = new(StringComparer.Ordinal);
        public List<string> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var key = $"{request.Method.Method} {request.RequestUri!.PathAndQuery}";
            Requests.Add(key);
            if (request.Content is not null)
            {
                Bodies[key] = await request.Content.ReadAsStringAsync(cancellationToken);
            }

            var found = Responses.TryGetValue(key, out var body);
            var status = StatusCodes.TryGetValue(key, out var configured)
                ? configured
                : found ? HttpStatusCode.OK : HttpStatusCode.NotFound;
            return new HttpResponseMessage(status)
            {
                Content = new StringContent(body ?? $"missing stub: {key}", Encoding.UTF8, "application/json"),
            };
        }
    }
}
