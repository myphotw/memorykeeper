using System.Text.Json;
using MemoryKeeper.Application;
using MemoryKeeper.Application.DTOs;
using MemoryKeeper.Domain.Enums;
using GalleryPhotoDetailDto = MemoryKeeper.Application.DTOs.Gallery.PhotoDetailDto;
using GalleryPhotoDto = MemoryKeeper.Application.DTOs.Gallery.PhotoDto;

namespace MemoryKeeper.App.Services;

/// <summary>
/// Maps TC-Backend Gallery DTOs into existing App/UI DTOs.
/// </summary>
public static class GalleryBackendMapper
{
    public static GalleryMediaDto ToGalleryMedia(GalleryPhotoDto photo, string apiBaseUrl)
    {
        var thumbRaw = photo.ThumbnailUrl;
        var previewRaw = photo.PreviewUrl;
        var thumb = ToAbsoluteUrl(apiBaseUrl, thumbRaw);
        var preview = ToAbsoluteUrl(apiBaseUrl, previewRaw);
        var id = ParseFileId(photo.FileId);

        return new GalleryMediaDto
        {
            Id = id,
            BackendFileId = photo.FileId ?? string.Empty,
            FileName = photo.Filename,
            AbsoluteLibraryPath = preview ?? thumb ?? string.Empty,
            CapturedAt = photo.CaptureDatetime,
            PlaceId = null,
            MediaType = MediaType.Photo,
            IsFavorite = photo.Favorite,
            ThumbnailUrl = thumb,
            PreviewUrl = preview,
        };
    }

    public static PhotoDetailDto ToPhotoDetail(GalleryPhotoDetailDto detail, string apiBaseUrl)
    {
        var metadata = detail.Metadata;
        var preview = ToAbsoluteUrl(apiBaseUrl, detail.PreviewUrl);
        var original = ToAbsoluteUrl(apiBaseUrl, detail.OriginalUrl) ?? string.Empty;
        var thumb = ToAbsoluteUrl(apiBaseUrl, detail.ThumbnailUrl);
        double? lat = GetDouble(metadata, "gps_lat");
        double? lon = GetDouble(metadata, "gps_lon");
        var mediaId = ParseFileId(detail.FileId);

        return new PhotoDetailDto
        {
            MediaId = mediaId,
            ThumbnailPath = thumb,
            ThumbnailUrl = thumb,
            PreviewUrl = preview,
            OriginalPath = original,
            RelativePath = detail.StoragePath ?? string.Empty,
            // Viewer/display: preview only (not original). Thumbnail is separate fallback.
            AbsoluteLibraryPath = preview ?? string.Empty,
            FileName = detail.Filename,
            CapturedAt = GetDate(metadata, "datetime_original"),
            Country = GetString(metadata, "country") ?? string.Empty,
            Province = GetString(metadata, "province") ?? string.Empty,
            City = GetString(metadata, "city") ?? string.Empty,
            Address = GetString(metadata, "district") ?? string.Empty,
            Latitude = lat,
            Longitude = lon,
            PlaceId = null,
            PlaceName = GetString(metadata, "place_name") ?? string.Empty,
            HasGps = lat is not null && lon is not null,
            IsFavorite = detail.Favorite,
            Width = detail.Width ?? GetInt(metadata, "image_width"),
            Height = detail.Height ?? GetInt(metadata, "image_height"),
            CameraMaker = GetString(metadata, "camera_make"),
            CameraModel = GetString(metadata, "camera_model"),
            Lens = GetString(metadata, "lens"),
            Iso = GetString(metadata, "iso"),
            Exposure = GetString(metadata, "exposure_time"),
            FNumber = GetString(metadata, "f_number"),
            FocalLength = GetString(metadata, "focal_length"),
            FileSizeBytes = detail.FileSize,
            Memo = string.Empty,
            Tags = MapTags(detail),
            RelatedPhotos = [],
        };
    }

    public static Guid ParseFileId(string fileId) => BackendFileIdCodec.ToGuid(fileId);

    public static string ToApiFileId(Guid id) => BackendFileIdCodec.ToApiFileId(id);

    public static string? ToAbsoluteUrl(string apiBaseUrl, string? pathOrUrl)
    {
        if (string.IsNullOrWhiteSpace(pathOrUrl))
        {
            return null;
        }

        var trimmed = pathOrUrl.Trim();
        if (Uri.TryCreate(trimmed, UriKind.Absolute, out var absolute)
            && (absolute.Scheme == Uri.UriSchemeHttp || absolute.Scheme == Uri.UriSchemeHttps))
        {
            return absolute.ToString();
        }

        var baseUrl = (apiBaseUrl ?? string.Empty).TrimEnd('/');
        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            return trimmed.StartsWith('/') ? trimmed : "/" + trimmed;
        }

        return trimmed.StartsWith('/')
            ? baseUrl + trimmed
            : baseUrl + "/" + trimmed;
    }

    private static IReadOnlyList<TagDto> MapTags(GalleryPhotoDetailDto detail)
    {
        return detail.UserTags
            .Concat(detail.AiTags)
            .Select(tag => new TagDto
            {
                Id = Guid.NewGuid(),
                Name = tag.Tag,
            })
            .ToList();
    }

    private static string? GetString(Dictionary<string, JsonElement> metadata, string key)
    {
        if (!metadata.TryGetValue(key, out var el)
            || el.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return null;
        }

        return el.ValueKind == JsonValueKind.String ? el.GetString() : el.ToString();
    }

    private static double? GetDouble(Dictionary<string, JsonElement> metadata, string key)
    {
        if (!metadata.TryGetValue(key, out var el))
        {
            return null;
        }

        return el.ValueKind switch
        {
            JsonValueKind.Number => el.GetDouble(),
            JsonValueKind.String when double.TryParse(el.GetString(), out var v) => v,
            _ => null,
        };
    }

    private static int? GetInt(Dictionary<string, JsonElement> metadata, string key)
    {
        if (!metadata.TryGetValue(key, out var el))
        {
            return null;
        }

        return el.ValueKind switch
        {
            JsonValueKind.Number => el.TryGetInt32(out var i) ? i : (int)el.GetDouble(),
            JsonValueKind.String when int.TryParse(el.GetString(), out var v) => v,
            _ => null,
        };
    }

    private static DateTimeOffset? GetDate(Dictionary<string, JsonElement> metadata, string key)
    {
        var text = GetString(metadata, key);
        return DateTimeOffset.TryParse(text, out var value) ? value : null;
    }
}
