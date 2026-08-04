using MemoryKeeper.Application.DTOs;
using MemoryKeeper.Application.Interfaces;
using MemoryKeeper.Domain.Enums;
using Microsoft.Extensions.Logging;

namespace MemoryKeeper.Infrastructure.Metadata;

public sealed class MetadataExtractorService : IMetadataExtractor
{
    private readonly IFileScanner _fileScanner;
    private readonly ExifReader _exifReader;
    private readonly ILogger<MetadataExtractorService> _logger;

    public MetadataExtractorService(
        IFileScanner fileScanner,
        ILogger<MetadataExtractorService> logger)
    {
        _fileScanner = fileScanner;
        _exifReader = new ExifReader();
        _logger = logger;
    }

    public Task<MediaMetadataDto> ExtractAsync(string filePath, CancellationToken cancellationToken = default)
    {
        return Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();

            var mediaType = _fileScanner.ResolveMediaType(filePath) ?? MediaType.Photo;
            try
            {
                var model = _exifReader.Read(filePath);
                var dto = _exifReader.ToDto(model, mediaType);
                _logger.LogInformation(
                    "[EXIF] Loaded. Path={Path}, DateSource={DateSource}, CapturedAt={CapturedAt}, GpsFormat={GpsFormat}, HasGps={HasGps}, Maker={Maker}, Model={Model}",
                    filePath,
                    dto.CaptureDateSource,
                    dto.CapturedAt,
                    dto.GpsFormat,
                    dto.Latitude is not null && dto.Longitude is not null,
                    dto.CameraMaker,
                    dto.CameraModel);
                return dto;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "[EXIF] Extract failed. Path={Path}, Message={Message}, InnerException={Inner}",
                    filePath,
                    ex.Message,
                    ex.InnerException?.Message);

                DateTimeOffset? fileCreatedAt = null;
                DateTimeOffset? fileModifiedAt = null;
                try
                {
                    fileCreatedAt = new DateTimeOffset(DateTime.SpecifyKind(File.GetCreationTime(filePath), DateTimeKind.Local));
                    fileModifiedAt = new DateTimeOffset(DateTime.SpecifyKind(File.GetLastWriteTime(filePath), DateTimeKind.Local));
                }
                catch
                {
                    // optional
                }

                return new MediaMetadataDto
                {
                    MediaType = mediaType,
                    CapturedAt = MediaMetadataDto.ResolveCapturedAt(null, fileCreatedAt, fileModifiedAt),
                    CaptureDateSource = fileCreatedAt is not null ? "FileCreated" : fileModifiedAt is not null ? "FileModified" : "None",
                    FileCreatedAt = fileCreatedAt,
                    FileModifiedAt = fileModifiedAt
                };
            }
        }, cancellationToken);
    }

    public Task<IReadOnlyDictionary<string, string>> DumpTagsAsync(
        string filePath,
        CancellationToken cancellationToken = default)
    {
        return Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var model = _exifReader.Read(filePath);
            return model.TagDump;
        }, cancellationToken);
    }
}
