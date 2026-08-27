using MemoryKeeper.Application.DTOs.Upload;
using MemoryKeeper.Application.Interfaces;
using MemoryKeeper.Application.Services;
using MemoryKeeper.Infrastructure.Services.Api;

namespace MemoryKeeper.Infrastructure.Repositories.Api;

/// <summary>
/// Uploads files to TC-Backend <c>POST /api/common/upload</c>. No SQLite access.
/// </summary>
public sealed class UploadApiRepository : IUploadApiRepository
{
    private const string UploadPath = "/api/common/upload";

    private readonly BaseApiClient _apiClient;

    public UploadApiRepository(BaseApiClient apiClient)
    {
        _apiClient = apiClient ?? throw new ArgumentNullException(nameof(apiClient));
    }

    public async Task<UploadResponseDto> UploadAsync(string filePath, CancellationToken cancellationToken = default)
    {
        return await UploadCoreAsync(filePath, fields: null, cancellationToken).ConfigureAwait(false);
    }

    public Task<UploadResponseDto> UploadWithIdentityAsync(
        string filePath,
        string clientFileId,
        string contentSha256,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(clientFileId);
        if (!ImportBackendIdentityProvider.IsSha256(contentSha256))
        {
            throw new ArgumentException("A valid lowercase SHA-256 is required.", nameof(contentSha256));
        }

        return UploadCoreAsync(
            filePath,
            new Dictionary<string, string>
            {
                ["service_name"] = "MemoryKeeper",
                ["client_file_id"] = clientFileId,
                ["client_content_sha256"] = contentSha256.ToLowerInvariant(),
            },
            cancellationToken);
    }

    private async Task<UploadResponseDto> UploadCoreAsync(
        string filePath,
        IReadOnlyDictionary<string, string>? fields,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            throw new ArgumentException("File path is required.", nameof(filePath));
        }

        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException("Upload source file was not found.", filePath);
        }

        var fileName = Path.GetFileName(filePath);
        await using var stream = new FileStream(
            filePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 1024 * 64,
            options: FileOptions.Asynchronous | FileOptions.SequentialScan);

        var response = fields is null
            ? await _apiClient.UploadAsync<UploadResponseDto>(
                    UploadPath,
                    stream,
                    fileName,
                    fieldName: "file",
                    contentType: GuessContentType(fileName),
                    cancellationToken)
                .ConfigureAwait(false)
            : await _apiClient.UploadWithFieldsAsync<UploadResponseDto>(
                    UploadPath,
                    stream,
                    fileName,
                    fields,
                    fieldName: "file",
                    contentType: GuessContentType(fileName),
                    cancellationToken)
                .ConfigureAwait(false);

        var data = response.Data
            ?? throw new ApiException(
                System.Net.HttpStatusCode.BadGateway,
                "Upload API returned an empty body.");

        if (string.IsNullOrWhiteSpace(data.Message) && !string.IsNullOrWhiteSpace(data.IncomingPath))
        {
            return new UploadResponseDto
            {
                JobId = data.JobId,
                Status = data.Status,
                Message = data.IncomingPath,
                Id = data.Id,
                IncomingPath = data.IncomingPath,
            };
        }

        return data;
    }

    private static string GuessContentType(string fileName)
    {
        var ext = Path.GetExtension(fileName).ToLowerInvariant();
        return ext switch
        {
            ".jpg" or ".jpeg" => "image/jpeg",
            ".png" => "image/png",
            ".gif" => "image/gif",
            ".webp" => "image/webp",
            ".heic" => "image/heic",
            ".tif" or ".tiff" => "image/tiff",
            ".bmp" => "image/bmp",
            _ => "application/octet-stream",
        };
    }
}
