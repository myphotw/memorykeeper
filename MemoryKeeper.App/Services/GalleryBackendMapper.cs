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
            PlaceId = photo.MemorykeeperPlaceId,
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
        var rawPlaceName = FirstNotEmpty(
            detail.GeocodedPlaceName,
            GetString(metadata, "geocoded_place_name"),
            GetString(metadata, "place_name"));
        var displayPlaceName = FirstNotEmpty(
            detail.PlaceDisplayName,
            GetString(metadata, "place_display_name"),
            rawPlaceName);

        return new PhotoDetailDto
        {
            IsBackendOnly = true,
            MediaId = mediaId,
            ThumbnailPath = thumb,
            ThumbnailUrl = thumb,
            PreviewUrl = preview,
            OriginalPath = original,
            RelativePath = detail.StoragePath ?? string.Empty,
            // Viewer/display: preview only (not original). Thumbnail is separate fallback.
            AbsoluteLibraryPath = preview ?? string.Empty,
            FileName = detail.Filename,
            CapturedAt = GetFirstDate(
                metadata,
                "datetime_original",
                "datetime_digitized",
                "datetime",
                "capture_datetime"),
            Country = GetFirstString(metadata, "country", "reverse_geocoded_country"),
            Province = GetFirstString(metadata, "province", "state", "administrative_area_level_1"),
            City = GetFirstString(metadata, "city", "locality", "administrative_area_level_2"),
            District = GetFirstString(metadata, "district", "sublocality", "administrative_area_level_3"),
            Address = FirstNotEmpty(
                rawPlaceName,
                GetString(metadata, "address"),
                GetString(metadata, "formatted_address")),
            Latitude = lat,
            Longitude = lon,
            PlaceId = detail.MemorykeeperPlaceId ?? GetGuid(metadata, "memorykeeper_place_id"),
            PlaceName = displayPlaceName,
            GeocodedPlaceName = rawPlaceName,
            CanonicalName = FirstNotEmpty(
                detail.PlaceCanonicalName,
                GetString(metadata, "place_canonical_name")),
            PlaceMatchSource = detail.PlaceMatchSource ?? GetString(metadata, "place_match_source"),
            PlaceMatchDistanceM = detail.PlaceMatchDistanceM ?? GetDouble(metadata, "place_match_distance_m"),
            PlaceRevision = detail.PlaceRevision > 0
                ? detail.PlaceRevision
                : GetInt(metadata, "place_revision") ?? 0,
            MetadataRevision = detail.MetadataRevision,
            HasGps = lat is not null && lon is not null,
            IsFavorite = detail.Favorite,
            Width = detail.Width ?? GetInt(metadata, "image_width"),
            Height = detail.Height ?? GetInt(metadata, "image_height"),
            CameraMaker = FirstNotEmpty(GetString(metadata, "camera_make"), GetString(metadata, "make")),
            CameraModel = FirstNotEmpty(GetString(metadata, "camera_model"), GetString(metadata, "model")),
            Lens = FirstNotEmpty(GetString(metadata, "lens_model"), GetString(metadata, "lens")),
            Iso = GetString(metadata, "iso"),
            Exposure = GetString(metadata, "exposure_time"),
            FNumber = GetString(metadata, "f_number"),
            FocalLength = GetString(metadata, "focal_length"),
            FileSizeBytes = detail.FileSize,
            Memo = detail.Memo ?? string.Empty,
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
        return detail.Tags.Select(MapTag).ToList();
    }

    private static TagDto MapTag(MemoryKeeper.Application.DTOs.Gallery.GalleryTagDto tag)
    {
        var source = string.Equals(tag.Source, "USER", StringComparison.OrdinalIgnoreCase)
            ? TagSource.User
            : TagSource.Ai;
        var displayName = string.IsNullOrWhiteSpace(tag.DisplayName) ? tag.Tag : tag.DisplayName;
        var bytes = new byte[16];
        if (tag.TagId is int backendId)
        {
            BitConverter.GetBytes(backendId).CopyTo(bytes, 0);
        }
        else
        {
            BitConverter.GetBytes(StringComparer.OrdinalIgnoreCase.GetHashCode(tag.Identity ?? displayName)).CopyTo(bytes, 0);
        }

        return new TagDto
            {
                Id = new Guid(bytes),
                BackendId = tag.TagId,
                Identity = tag.Identity,
                Name = displayName,
                Source = source,
                IsAssigned = true,
                Revision = tag.Revision ?? 0,
                CanRemove = !string.IsNullOrWhiteSpace(tag.Identity),
            };
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

    private static Guid? GetGuid(Dictionary<string, JsonElement> metadata, string key) =>
        Guid.TryParse(GetString(metadata, key), out var value) ? value : null;

    private static string FirstNotEmpty(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim() ?? string.Empty;

    private static string GetFirstString(Dictionary<string, JsonElement> metadata, params string[] keys) =>
        FirstNotEmpty(keys.Select(key => GetString(metadata, key)).ToArray());

    private static DateTimeOffset? GetFirstDate(
        Dictionary<string, JsonElement> metadata,
        params string[] keys)
    {
        foreach (var key in keys)
        {
            var text = GetString(metadata, key);
            if (DateTimeOffset.TryParse(text, out var value))
            {
                return value;
            }

            if (DateTime.TryParseExact(
                    text,
                    "yyyy:MM:dd HH:mm:ss",
                    System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.AssumeLocal,
                    out var exifDate))
            {
                return new DateTimeOffset(exifDate);
            }
        }

        return null;
    }
}
