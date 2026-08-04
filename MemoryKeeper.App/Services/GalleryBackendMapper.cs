using System.Text.Json;
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
        var thumb = ToAbsoluteUrl(apiBaseUrl, photo.ThumbnailUrl);
        var preview = ToAbsoluteUrl(apiBaseUrl, photo.PreviewUrl);

        return new GalleryMediaDto
        {
            Id = ParseFileId(photo.FileId),
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
        var original = ToAbsoluteUrl(apiBaseUrl, detail.OriginalUrl) ?? preview ?? string.Empty;
        var thumb = ToAbsoluteUrl(apiBaseUrl, detail.ThumbnailUrl);
        double? lat = GetDouble(metadata, "gps_lat");
        double? lon = GetDouble(metadata, "gps_lon");

        return new PhotoDetailDto
        {
            MediaId = ParseFileId(detail.FileId),
            ThumbnailPath = thumb,
            OriginalPath = original,
            RelativePath = detail.StoragePath ?? string.Empty,
            AbsoluteLibraryPath = preview ?? original,
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

    public static Guid ParseFileId(string fileId) =>
        Guid.TryParse(fileId, out var id) ? id : Guid.Empty;

    public static string? ToAbsoluteUrl(string apiBaseUrl, string? pathOrUrl)
    {
        if (string.IsNullOrWhiteSpace(pathOrUrl))
        {
            return null;
        }

        if (Uri.TryCreate(pathOrUrl, UriKind.Absolute, out _))
        {
            return pathOrUrl;
        }

        var baseUrl = apiBaseUrl.TrimEnd('/');
        return pathOrUrl.StartsWith('/')
            ? baseUrl + pathOrUrl
            : baseUrl + "/" + pathOrUrl;
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
