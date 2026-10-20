using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MemoryKeeper.Mobile.Configuration;
using MemoryKeeper.Mobile.Http;
using MemoryKeeper.Mobile.Uploads;

namespace MemoryKeeper.Tests.UnitTests;

public sealed class MobileUploadFoundationTests
{
    private static readonly Uri BackendUri = new("https://backend.example:8443/");
    private const string ValidSha = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

    [Fact]
    public void AndroidPicker_RequiresOriginalPhotoBytesAndStagesPhotosAndVideos()
    {
        var picker = ReadSource(
            "MemoryKeeper.Mobile", "Platforms", "Android", "Uploads", "AndroidOriginalMediaPicker.cs");
        var activity = ReadSource(
            "MemoryKeeper.Mobile", "Platforms", "Android", "MainActivity.cs");
        var manifest = ReadSource(
            "MemoryKeeper.Mobile", "Platforms", "Android", "AndroidManifest.xml");
        var startup = ReadSource("MemoryKeeper.Mobile", "MauiProgram.cs");

        Assert.Contains("MediaStore.ActionPickImages", activity, StringComparison.Ordinal);
        Assert.Contains("MediaStore.ExtraPickImagesMax", activity, StringComparison.Ordinal);
        Assert.Contains(
            "android.provider.extra.REQUEST_LOCATION_METADATA_ACCESS",
            activity,
            StringComparison.Ordinal);
        Assert.Contains("Intent.ExtraLocalOnly", activity, StringComparison.Ordinal);
        Assert.Contains("Intent.ActionGetContent", activity, StringComparison.Ordinal);
        Assert.Contains("Intent.ExtraAllowMultiple", activity, StringComparison.Ordinal);
        Assert.Contains("\"image/*\"", activity, StringComparison.Ordinal);
        Assert.Contains("\"video/*\"", activity, StringComparison.Ordinal);
        Assert.Contains("ACCESS_MEDIA_LOCATION", manifest, StringComparison.Ordinal);
        Assert.Contains("MediaStore.SetRequireOriginal", picker, StringComparison.Ordinal);
        Assert.Contains("MediaStore.GetMediaUri", picker, StringComparison.Ordinal);
        Assert.Contains("MediaDataColumn", picker, StringComparison.Ordinal);
        Assert.Contains("ResolveOriginalFileName", picker, StringComparison.Ordinal);
        Assert.Contains("_stagingStore.StageAsync", picker, StringComparison.Ordinal);
        Assert.Contains("MK_UPLOAD_ORIGINAL", picker, StringComparison.Ordinal);
        Assert.Contains("AndroidOriginalMediaPicker", startup, StringComparison.Ordinal);
        Assert.DoesNotContain("BitmapFactory", picker, StringComparison.Ordinal);
        Assert.DoesNotContain("Compress", picker, StringComparison.Ordinal);
        Assert.DoesNotContain("ExifInterface", picker, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(28, true, false, false, false, AndroidOriginalMediaReadMode.Direct)]
    [InlineData(34, false, false, false, false, AndroidOriginalMediaReadMode.Direct)]
    [InlineData(34, true, false, true, true, AndroidOriginalMediaReadMode.PermissionRequired)]
    [InlineData(34, true, true, true, false, AndroidOriginalMediaReadMode.LocationAwarePhotoPicker)]
    [InlineData(34, true, true, false, false, AndroidOriginalMediaReadMode.UnsupportedProvider)]
    [InlineData(34, true, true, false, true, AndroidOriginalMediaReadMode.RequireMediaStoreOriginal)]
    public void AndroidOriginalAccessPolicy_SelectsSafeReadMode(
        int apiLevel,
        bool isPhoto,
        bool hasPermission,
        bool usedLocationAwarePhotoPicker,
        bool hasMediaStoreUri,
        AndroidOriginalMediaReadMode expected)
    {
        Assert.Equal(expected, AndroidOriginalMediaAccessPolicy.Decide(
            apiLevel,
            isPhoto,
            hasPermission,
            usedLocationAwarePhotoPicker,
            hasMediaStoreUri));
    }

    [Theory]
    [InlineData(32, 23, false)]
    [InlineData(33, 22, false)]
    [InlineData(33, 23, true)]
    [InlineData(37, 23, true)]
    public void AndroidPhotoPicker_RequiresLocationMetadataCapablePlatform(
        int apiLevel,
        int extensionVersion,
        bool expected)
    {
        Assert.Equal(
            expected,
            AndroidOriginalMediaAccessPolicy.SupportsLocationAwarePhotoPicker(
                apiLevel,
                extensionVersion));
    }

    [Theory]
    [InlineData(
        "/storage/emulated/0/DCIM/Camera/20261008_141830.jpg",
        "18300.jpg",
        "18300",
        "20261008_141830.jpg")]
    [InlineData(null, "20261008_141830.jpg", "18300", "20261008_141830.jpg")]
    [InlineData(null, "제주 여행 사진 01.jpg", "18300", "제주 여행 사진 01.jpg")]
    public void AndroidOriginalFileName_PrefersMediaPathAndPreservesDisplayName(
        string? mediaDataPath,
        string? displayName,
        string? uriLastPathSegment,
        string expected)
    {
        Assert.Equal(
            expected,
            AndroidOriginalMediaAccessPolicy.ResolveOriginalFileName(
                mediaDataPath,
                displayName,
                uriLastPathSegment));
    }

    [Theory]
    [InlineData(null, null, "18300")]
    [InlineData(null, "18300.jpg", "18300")]
    [InlineData(null, "18300", "18300")]
    public void AndroidOriginalFileName_ReturnsNullWhenMetadataCannotIdentifyOriginal(
        string? mediaDataPath,
        string? displayName,
        string? uriLastPathSegment)
    {
        Assert.Null(AndroidOriginalMediaAccessPolicy.ResolveOriginalFileName(
            mediaDataPath,
            displayName,
            uriLastPathSegment));
    }

    [Fact]
    public async Task StagedOriginal_IsByteIdenticalForHashAndUploadAndIsReleased()
    {
        var root = Path.Combine(Path.GetTempPath(), $"memorykeeper-upload-test-{Guid.NewGuid():N}");
        try
        {
            var bytes = Enumerable.Range(0, 512_123).Select(index => (byte)(index % 251)).ToArray();
            var store = new MobileUploadStagingStore(root);
            await using var original = new MemoryStream(bytes, writable: false);
            var staged = await store.StageAsync("gps-photo.jpg", "image/jpeg", original);

            var hash = await new MobileContentHasher().ComputeSha256Async(staged);
            using var uploaded = new MemoryStream();
            await using (var uploadStream = await staged.OpenReadAsync())
            {
                await uploadStream.CopyToAsync(uploaded);
            }

            Assert.Equal(bytes.Length, staged.FileSize);
            Assert.Equal("gps-photo.jpg", staged.FileName);
            Assert.Equal(Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(), hash.Sha256);
            Assert.Equal(bytes, uploaded.ToArray());
            Assert.Single(Directory.EnumerateFiles(root, "*.upload"));

            await staged.ReleaseAsync();

            Assert.Empty(Directory.EnumerateFiles(root, "*.upload"));
            var released = await Assert.ThrowsAsync<MobileUploadException>(
                () => staged.OpenReadAsync());
            Assert.Equal(MobileUploadErrorCodes.FileAccess, released.Code);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public async Task StagingCancellation_RemovesPartialPrivateCopy()
    {
        var root = Path.Combine(Path.GetTempPath(), $"memorykeeper-upload-test-{Guid.NewGuid():N}");
        try
        {
            using var cancellation = new CancellationTokenSource();
            await using var original = new CancelingReadStream(
                new byte[MobileUploadStagingStore.BufferSize * 2],
                cancellation);
            var store = new MobileUploadStagingStore(root);

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => store.StageAsync(
                "canceled.jpg",
                "image/jpeg",
                original,
                cancellation.Token));

            Assert.True(Directory.Exists(root));
            Assert.Empty(Directory.EnumerateFiles(root, "*.upload"));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public async Task GpsFreeJpeg_IsStagedWithoutExifInspectionOrRewriting()
    {
        var root = Path.Combine(Path.GetTempPath(), $"memorykeeper-upload-test-{Guid.NewGuid():N}");
        try
        {
            byte[] jpegWithoutGps = [0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x02, 0xFF, 0xD9];
            var store = new MobileUploadStagingStore(root);
            await using var original = new MemoryStream(jpegWithoutGps, writable: false);
            var staged = await store.StageAsync("without-gps.jpg", "image/jpeg", original);
            using var copied = new MemoryStream();
            await using (var reopened = await staged.OpenReadAsync())
            {
                await reopened.CopyToAsync(copied);
            }

            Assert.Equal(jpegWithoutGps, copied.ToArray());
            await staged.ReleaseAsync();
            Assert.Empty(Directory.EnumerateFiles(root, "*.upload"));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public async Task Hasher_ComputesLowercaseSha256AndDisposesEachSourceStream()
    {
        var bytes = Encoding.UTF8.GetBytes("MemoryKeeper mobile upload");
        var streams = new List<TrackingMemoryStream>();
        var file = new MobileSelectedMediaFile(
            "photo.jpg",
            "image/jpeg",
            bytes.Length,
            _ =>
            {
                var stream = new TrackingMemoryStream(bytes);
                streams.Add(stream);
                return Task.FromResult<Stream>(stream);
            });

        var result = await new MobileContentHasher().ComputeSha256Async(file);

        Assert.Equal(Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(), result.Sha256);
        Assert.Equal(bytes.Length, result.BytesRead);
        Assert.True(MobileUploadContractValidation.IsLowercaseSha256(result.Sha256));
        Assert.True(Assert.Single(streams).Disposed);
    }

    [Fact]
    public async Task Hasher_UsesBoundedReadsForLargeNonSeekableStream()
    {
        const long length = 12L * 1024 * 1024 + 17;
        var stream = new GeneratedReadStream(length);
        var file = new MobileSelectedMediaFile(
            "video.mp4",
            "video/mp4",
            length,
            _ => Task.FromResult<Stream>(stream));

        var result = await new MobileContentHasher().ComputeSha256Async(file);

        Assert.Equal(length, result.BytesRead);
        Assert.InRange(stream.MaximumRequestedRead, 1, MobileContentHasher.BufferSize);
        Assert.True(stream.Disposed);
    }

    [Fact]
    public async Task SelectedFile_ReopenFailureReturnsReselectableFileAccessError()
    {
        var file = new MobileSelectedMediaFile(
            "expired-photo.jpg",
            "image/jpeg",
            123,
            _ => Task.FromException<Stream>(new IOException("provider permission expired")));

        var error = await Assert.ThrowsAsync<MobileUploadException>(() => file.OpenReadAsync());

        Assert.Equal(MobileUploadErrorCodes.FileAccess, error.Code);
        Assert.Contains("다시 선택", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void UploadFile_UsesUuidSeparateFromShaAndKeepsItForRetry()
    {
        var selected = CreateSelectedFile("photo.jpg", "image/jpeg", [1, 2, 3]);
        var firstAttempt = new MobileUploadFile(selected);
        var anotherUpload = new MobileUploadFile(selected);

        firstAttempt.SetContentSha256(ValidSha);
        var retryReference = firstAttempt;

        Assert.True(Guid.TryParse(firstAttempt.ClientFileId, out _));
        Assert.Equal(firstAttempt.ClientFileId, retryReference.ClientFileId);
        Assert.NotEqual(firstAttempt.ClientFileId, anotherUpload.ClientFileId);
        Assert.Equal(ValidSha, firstAttempt.ClientContentSha256);
        Assert.NotEqual(firstAttempt.ClientFileId, firstAttempt.ClientContentSha256);
    }

    [Fact]
    public async Task Capabilities_LegacyMobileContractAllowsUploadWithoutPreflight()
    {
        var handler = JsonHandler(HttpStatusCode.OK, """
            {
              "api_version":"1.1",
              "capabilities":{"upload":true},
              "supports_upload_source":true,
              "supports_client_file_id":true,
              "supports_client_content_sha256":true
            }
            """);
        var client = CreateApiClient(handler);

        var result = await client.GetCapabilitiesAsync();

        Assert.True(result.CanUpload);
        Assert.False(result.SupportsPreflight);
        Assert.Null(result.ResultCode);
    }

    [Fact]
    public async Task Capabilities_MissingRequiredMobileContractReturnsServerUpdateRequired()
    {
        var handler = JsonHandler(HttpStatusCode.OK, """
            {
              "api_version":"1.1",
              "capabilities":{"upload":true},
              "upload_contract":{
                "supports_upload_source":false,
                "supports_client_file_id":true,
                "supports_client_content_sha256":true
              }
            }
            """);
        var client = CreateApiClient(handler);

        var result = await client.GetCapabilitiesAsync();

        Assert.False(result.CanUpload);
        Assert.Equal(MobileUploadErrorCodes.ServerUpdateRequired, result.ResultCode);

        var preflight = await client.PreflightAsync(result, ValidSha);
        Assert.Equal(MobileUploadPreflightDisposition.BlockedServerUpdate, preflight.Disposition);
        Assert.False(preflight.ShouldUpload);
    }

    [Fact]
    public async Task Capabilities_MapsPhase2PreflightAndFailureCodes()
    {
        var handler = JsonHandler(HttpStatusCode.OK, """
            {
              "api_version":"1.1",
              "capabilities":{"upload":true},
              "supports_upload_source":true,
              "supports_client_file_id":true,
              "supports_client_content_sha256":true,
              "supports_mobile_sha256_preflight":true,
              "mobile_sha256_preflight_endpoint":"/api/common/upload/preflight",
              "upload_failure_codes":["DUPLICATE_CONTENT","CONTENT_SHA256_MISMATCH"]
            }
            """);
        var client = CreateApiClient(handler);

        var result = await client.GetCapabilitiesAsync();

        Assert.True(result.CanUpload);
        Assert.True(result.SupportsPreflight);
        Assert.Equal("/api/common/upload/preflight", result.PreflightPath);
        Assert.Contains(MobileUploadErrorCodes.DuplicateContent, result.FailureCodes);
        Assert.Contains(MobileUploadErrorCodes.ContentSha256Mismatch, result.FailureCodes);
    }

    [Fact]
    public async Task Capabilities_AbsentEndpointUsesLegacyDirectUploadFallback()
    {
        var client = CreateApiClient(JsonHandler(HttpStatusCode.NotFound, "{}"));

        var result = await client.GetCapabilitiesAsync();

        Assert.True(result.CanUpload);
        Assert.True(result.IsLegacyUnknown);
        Assert.False(result.SupportsPreflight);
    }

    [Theory]
    [InlineData(true, false, "DUPLICATE_CONTENT", MobileUploadPreflightDisposition.Duplicate)]
    [InlineData(false, true, null, MobileUploadPreflightDisposition.UploadAllowed)]
    public async Task Preflight_MapsDuplicateAndAllowedResponses(
        bool duplicate,
        bool uploadAllowed,
        string? reason,
        MobileUploadPreflightDisposition expected)
    {
        var body = JsonSerializer.Serialize(new
        {
            duplicate,
            upload_allowed = uploadAllowed,
            reason,
        });
        var client = CreateApiClient(JsonHandler(HttpStatusCode.OK, body));

        var result = await client.PreflightAsync(SupportedCapabilities(), ValidSha);

        Assert.Equal(expected, result.Disposition);
        Assert.Equal(!duplicate, result.ShouldUpload);
    }

    [Fact]
    public async Task Preflight_UnsupportedOrUnavailableNeverPretendsToBeDuplicate()
    {
        var unusedHandler = new DelegateHandler((_, _) =>
            throw new InvalidOperationException("No request was expected."));
        var unsupportedClient = CreateApiClient(unusedHandler);
        var unsupported = await unsupportedClient.PreflightAsync(
            MobileUploadBatchCapabilities.LegacyUnknown,
            ValidSha);

        var missingClient = CreateApiClient(JsonHandler(HttpStatusCode.NotFound, "{}"));
        var missing = await missingClient.PreflightAsync(SupportedCapabilities(), ValidSha);

        var networkClient = CreateApiClient(new DelegateHandler((_, _) =>
            throw new HttpRequestException("offline")));
        var network = await networkClient.PreflightAsync(SupportedCapabilities(), ValidSha);

        Assert.Equal(MobileUploadPreflightDisposition.SkippedUnsupported, unsupported.Disposition);
        Assert.Equal(MobileUploadPreflightDisposition.SkippedUnsupported, missing.Disposition);
        Assert.Equal(MobileUploadPreflightDisposition.SkippedUnavailable, network.Disposition);
        Assert.All(new[] { unsupported, missing, network }, result => Assert.True(result.ShouldUpload));
    }

    [Fact]
    public void PreflightEndpoint_RejectsAbsoluteForeignOrNonApiPaths()
    {
        Assert.False(MobileUploadContractValidation.TryNormalizeApiPath(
            "https://evil.example/api/common/upload/preflight",
            out _));
        Assert.False(MobileUploadContractValidation.TryNormalizeApiPath("/health", out _));
        Assert.True(MobileUploadContractValidation.TryNormalizeApiPath(
            "/api/common/upload/preflight",
            out var path));
        Assert.Equal("/api/common/upload/preflight", path);
    }

    [Fact]
    public async Task MultipartUpload_SendsOriginalStreamIdentityFieldsAndBearer()
    {
        var fileBytes = Encoding.UTF8.GetBytes("not-buffered-by-the-upload-model");
        var selected = CreateSelectedFile("clip.mp4", "video/mp4", fileBytes);
        var upload = new MobileUploadFile(selected, "11111111-2222-3333-4444-555555555555");
        upload.SetContentSha256(ValidSha);
        var fields = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        var progressValues = new List<MobileUploadProgress>();
        AuthenticationHeaderValue? authorization = null;
        string? uploadedFileName = null;
        var terminal = new DelegateHandler(async (request, cancellationToken) =>
        {
            authorization = request.Headers.Authorization;
            var multipart = Assert.IsType<MultipartFormDataContent>(request.Content);
            foreach (var part in multipart)
            {
                var name = part.Headers.ContentDisposition?.Name?.Trim('"');
                Assert.False(string.IsNullOrWhiteSpace(name));
                if (string.Equals(name, "file", StringComparison.Ordinal))
                {
                    uploadedFileName = part.Headers.ContentDisposition?.FileName?.Trim('"');
                }

                fields[name!] = await part.ReadAsByteArrayAsync(cancellationToken);
            }

            return JsonResponse(HttpStatusCode.OK, """
                {"job_id":"aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee","status":"WAITING"}
                """);
        });
        var client = CreateAuthenticatedApiClient(terminal);

        var response = await client.UploadAsync(
            upload,
            progress: new InlineProgress<MobileUploadProgress>(progressValues.Add));

        Assert.Equal("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee", response.JobId);
        Assert.Equal(fileBytes, fields["file"]);
        Assert.Equal("clip.mp4", uploadedFileName);
        Assert.Equal("MemoryKeeper", Encoding.UTF8.GetString(fields["service_name"]));
        Assert.Equal("mobile", Encoding.UTF8.GetString(fields["upload_source"]));
        Assert.Equal(upload.ClientFileId, Encoding.UTF8.GetString(fields["client_file_id"]));
        Assert.Equal(ValidSha, Encoding.UTF8.GetString(fields["client_content_sha256"]));
        Assert.Equal("Bearer", authorization?.Scheme);
        Assert.Equal("credential-value", authorization?.Parameter);
        Assert.NotEmpty(progressValues);
        Assert.Equal(fileBytes.Length, progressValues[^1].BytesSent);
        Assert.Equal(fileBytes.Length, progressValues[^1].TotalBytes);
    }

    [Fact]
    public async Task HashThenUpload_ReopensSameSelectedSourceWithoutBufferingWholeFile()
    {
        var bytes = Enumerable.Range(0, 200_000).Select(index => (byte)(index % 251)).ToArray();
        var openCount = 0;
        var disposedCount = 0;
        var selected = new MobileSelectedMediaFile(
            "large-photo.jpg",
            "image/jpeg",
            bytes.Length,
            _ =>
            {
                openCount++;
                return Task.FromResult<Stream>(new CallbackMemoryStream(
                    bytes,
                    () => disposedCount++));
            });
        var upload = new MobileUploadFile(selected);
        var hash = await new MobileContentHasher().ComputeSha256Async(selected);
        upload.SetContentSha256(hash.Sha256);
        var client = CreateApiClient(new DelegateHandler(async (request, cancellationToken) =>
        {
            var multipart = Assert.IsType<MultipartFormDataContent>(request.Content);
            var filePart = multipart.Single(part =>
                string.Equals(
                    part.Headers.ContentDisposition?.Name?.Trim('"'),
                    "file",
                    StringComparison.Ordinal));
            Assert.Equal(bytes, await filePart.ReadAsByteArrayAsync(cancellationToken));
            return JsonResponse(HttpStatusCode.OK, """
                {"job_id":"aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee","status":"WAITING"}
                """);
        }));

        await client.UploadAsync(upload);

        Assert.Equal(2, openCount);
        Assert.Equal(2, disposedCount);
        var apiSource = ReadSource(
            "MemoryKeeper.Mobile", "Uploads", "MobileUploadApiClient.cs");
        Assert.Contains("new StreamContent(uploadSource)", apiSource, StringComparison.Ordinal);
        Assert.DoesNotContain("ReadAsByteArrayAsync", apiSource, StringComparison.Ordinal);
        Assert.DoesNotContain("MemoryStream", apiSource, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Upload409_DistinguishesStructuredCodesFromGeneralConflict()
    {
        var duplicateClient = CreateApiClient(JsonHandler(HttpStatusCode.Conflict, """
            {"detail":{"code":"DUPLICATE_CONTENT","message":"already registered"}}
            """));
        var mismatchClient = CreateApiClient(JsonHandler(HttpStatusCode.Conflict, """
            {"detail":{"code":"CONTENT_SHA256_MISMATCH","message":"hash mismatch"}}
            """));
        var conflictClient = CreateApiClient(JsonHandler(HttpStatusCode.Conflict, """
            {"detail":"client_file_id is already associated with different content"}
            """));
        var duplicate = UploadFile();
        var mismatch = UploadFile();
        var conflict = UploadFile();

        var duplicateError = await Assert.ThrowsAsync<MobileUploadException>(
            () => duplicateClient.UploadAsync(duplicate));
        var mismatchError = await Assert.ThrowsAsync<MobileUploadException>(
            () => mismatchClient.UploadAsync(mismatch));
        var conflictError = await Assert.ThrowsAsync<MobileUploadException>(
            () => conflictClient.UploadAsync(conflict));

        Assert.Equal(MobileUploadErrorCodes.DuplicateContent, duplicateError.Code);
        Assert.Equal(MobileUploadErrorCodes.ContentSha256Mismatch, mismatchError.Code);
        Assert.Equal(MobileUploadErrorCodes.Conflict, conflictError.Code);
    }

    [Fact]
    public async Task Preflight_AuthenticationFailureIsNotTreatedAsDuplicateOrUnsupported()
    {
        var client = CreateApiClient(JsonHandler(HttpStatusCode.Unauthorized, "{}"));

        var error = await Assert.ThrowsAsync<MobileUploadException>(
            () => client.PreflightAsync(SupportedCapabilities(), ValidSha));

        Assert.Equal(MobileUploadErrorCodes.AuthenticationRequired, error.Code);
        Assert.Equal(HttpStatusCode.Unauthorized, error.StatusCode);
    }

    [Fact]
    public async Task JobStatus_MapsFailureCodeWithoutParsingLastError()
    {
        var client = CreateApiClient(JsonHandler(HttpStatusCode.OK, """
            {
              "job_id":"aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee",
              "status":"FAILED",
              "progress":33,
              "last_error":"localized or arbitrary worker text",
              "failure_code":"CONTENT_SHA256_MISMATCH"
            }
            """));

        var status = await client.GetJobAsync(
            Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee"));

        Assert.True(status.Status.IsFailed);
        Assert.Equal(
            MobileUploadErrorCodes.ContentSha256Mismatch,
            status.Status.FailureCode);
        Assert.Equal(MobileUploadJobFailureKind.ContentSha256Mismatch, status.FailureKind);
        Assert.NotEqual(status.Status.LastError, status.Status.FailureCode);
    }

    [Fact]
    public async Task Upload_PropagatesCallerCancellation()
    {
        var client = CreateApiClient(new DelegateHandler(async (_, cancellationToken) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return JsonResponse(HttpStatusCode.OK, "{}");
        }));
        using var cancellation = new CancellationTokenSource();
        cancellation.CancelAfter(TimeSpan.FromMilliseconds(20));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => client.UploadAsync(UploadFile(), cancellation.Token));
    }

    private static MobileUploadFile UploadFile()
    {
        var upload = new MobileUploadFile(CreateSelectedFile(
            "photo.jpg",
            "image/jpeg",
            [1, 2, 3, 4]));
        upload.SetContentSha256(ValidSha);
        return upload;
    }

    private static MobileSelectedMediaFile CreateSelectedFile(
        string fileName,
        string contentType,
        byte[] bytes) => new(
        fileName,
        contentType,
        bytes.Length,
        _ => Task.FromResult<Stream>(new MemoryStream(bytes, writable: false)));

    private static MobileUploadBatchCapabilities SupportedCapabilities() => new(
        CanUpload: true,
        SupportsPreflight: true,
        PreflightPath: "/api/common/upload/preflight",
        IsLegacyUnknown: false,
        ResultCode: null,
        FailureCodes: new HashSet<string>(
            [MobileUploadErrorCodes.DuplicateContent, MobileUploadErrorCodes.ContentSha256Mismatch],
            StringComparer.Ordinal));

    private static MobileUploadApiClient CreateApiClient(HttpMessageHandler handler) => new(
        new TestHttpClientFactory(new HttpClient(handler)),
        new FixedEndpointResolver(BackendUri));

    private static MobileUploadApiClient CreateAuthenticatedApiClient(HttpMessageHandler terminal)
    {
        var configuration = new TestConfiguration(BackendUri, "credential-value");
        var authentication = new MobileBackendAuthenticationHandler(configuration)
        {
            InnerHandler = terminal,
        };
        return CreateApiClient(authentication);
    }

    private static DelegateHandler JsonHandler(HttpStatusCode statusCode, string json) =>
        new((_, _) => Task.FromResult(JsonResponse(statusCode, json)));

    private static HttpResponseMessage JsonResponse(HttpStatusCode statusCode, string json) => new(statusCode)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json"),
    };

    private static string ReadSource(params string[] parts) =>
        File.ReadAllText(Path.Combine([FindRepositoryRoot(), .. parts]));

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "MemoryKeeper.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
               ?? throw new DirectoryNotFoundException("MemoryKeeper repository root was not found.");
    }

    private sealed class TestHttpClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }

    private sealed class FixedEndpointResolver(Uri endpoint) : IMobileBackendEndpointResolver
    {
        public Uri? CurrentBaseUri => endpoint;

        public Task<Uri> ResolveAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(endpoint);

        public void Invalidate()
        {
        }
    }

    private sealed class TestConfiguration(Uri endpoint, string token) : IMobileBackendConfiguration
    {
        public Uri? InternalBaseUri => endpoint;
        public Uri? ExternalBaseUri => endpoint;
        public string? BearerToken => token;
        public bool IsConfigured => true;
    }

    private sealed class DelegateHandler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> sendAsync)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) => sendAsync(request, cancellationToken);
    }

    private sealed class InlineProgress<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value) => report(value);
    }

    private sealed class TrackingMemoryStream(byte[] bytes) : MemoryStream(bytes, writable: false)
    {
        public bool Disposed { get; private set; }

        protected override void Dispose(bool disposing)
        {
            Disposed = true;
            base.Dispose(disposing);
        }
    }

    private sealed class CallbackMemoryStream(byte[] bytes, Action onDispose)
        : MemoryStream(bytes, writable: false)
    {
        private bool _disposed;

        protected override void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                _disposed = true;
                onDispose();
            }

            base.Dispose(disposing);
        }
    }

    private sealed class CancelingReadStream(
        byte[] bytes,
        CancellationTokenSource cancellation) : MemoryStream(bytes, writable: false)
    {
        public override async ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            var read = await base.ReadAsync(buffer, cancellationToken);
            if (read > 0)
            {
                cancellation.Cancel();
            }

            return read;
        }
    }

    private sealed class GeneratedReadStream(long length) : Stream
    {
        private long _position;

        public int MaximumRequestedRead { get; private set; }

        public bool Disposed { get; private set; }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => _position;
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count) =>
            ReadAsync(buffer.AsMemory(offset, count)).AsTask().GetAwaiter().GetResult();

        public override ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            MaximumRequestedRead = Math.Max(MaximumRequestedRead, buffer.Length);
            var remaining = length - _position;
            if (remaining <= 0)
            {
                return ValueTask.FromResult(0);
            }

            var count = (int)Math.Min(buffer.Length, remaining);
            buffer.Span[..count].Fill(0x5A);
            _position += count;
            return ValueTask.FromResult(count);
        }

        protected override void Dispose(bool disposing)
        {
            Disposed = true;
            base.Dispose(disposing);
        }

        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
