using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using MemoryKeeper.Application.DTOs;
using MemoryKeeper.Application.DTOs.Gallery;
using MemoryKeeper.Application.Interfaces;

namespace MemoryKeeper.Application.Services;

/// <summary>Exports immutable NAS originals and writes MemoryKeeper metadata to XMP sidecars.</summary>
public sealed class PhotoExportService
{
    private readonly IPhotoExportSource _source;

    public PhotoExportService(IPhotoExportSource source) =>
        _source = source ?? throw new ArgumentNullException(nameof(source));

    public async Task<PhotoExportResultDto> ExportAsync(
        string destinationRoot,
        IProgress<PhotoExportProgressDto>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(destinationRoot))
        {
            throw new ArgumentException("내보낼 폴더를 선택해 주세요.", nameof(destinationRoot));
        }

        Directory.CreateDirectory(destinationRoot);
        var items = await _source.GetCatalogAsync(cancellationToken).ConfigureAwait(false);
        var exported = 0;
        var metadataPartial = 0;
        var copyFailed = 0;
        var completed = 0;

        foreach (var item in items)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var detail = await _source.GetDetailAsync(item.FileId, cancellationToken).ConfigureAwait(false);
                var directory = Path.Combine(
                    destinationRoot,
                    SafeSegment(item.Year, GalleryHierarchyService.UnclassifiedTitle),
                    SafeSegment(item.Country, GalleryHierarchyService.OtherTitle),
                    SafeSegment(item.Region, GalleryHierarchyService.OtherTitle),
                    SafeSegment(item.Place, GalleryHierarchyService.UnclassifiedTitle));
                Directory.CreateDirectory(directory);
                var targetPath = ResolveCollisionPath(directory, item.Filename);
                var temporaryPath = targetPath + ".partial";
                try
                {
                    await using (var output = new FileStream(
                                     temporaryPath,
                                     FileMode.CreateNew,
                                     FileAccess.Write,
                                     FileShare.None,
                                     81920,
                                     useAsync: true))
                    {
                        await _source.DownloadOriginalAsync(
                            item.FileId,
                            detail.OriginalUrl,
                            output,
                            cancellationToken).ConfigureAwait(false);
                    }

                    File.Move(temporaryPath, targetPath, overwrite: false);
                    exported++;
                }
                catch
                {
                    TryDelete(temporaryPath);
                    throw;
                }

                try
                {
                    var sidecarPath = Path.ChangeExtension(targetPath, ".xmp");
                    await File.WriteAllTextAsync(
                        sidecarPath,
                        BuildXmp(item, detail.Detail),
                        new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                        cancellationToken).ConfigureAwait(false);
                }
                catch (Exception) when (!cancellationToken.IsCancellationRequested)
                {
                    metadataPartial++;
                }
            }
            catch (Exception) when (!cancellationToken.IsCancellationRequested)
            {
                copyFailed++;
            }
            finally
            {
                completed++;
                progress?.Report(new PhotoExportProgressDto
                {
                    Total = items.Count,
                    Completed = completed,
                    Failed = copyFailed,
                    CurrentFileName = item.Filename,
                });
            }
        }

        return new PhotoExportResultDto
        {
            TotalCount = items.Count,
            ExportedCount = exported,
            MetadataPartialCount = metadataPartial,
            CopyFailedCount = copyFailed,
            DestinationPath = Path.GetFullPath(destinationRoot),
        };
    }

    internal static string ResolveCollisionPath(string directory, string filename)
    {
        var safeName = SafeSegment(Path.GetFileName(filename), "photo");
        var extension = Path.GetExtension(safeName);
        var stem = Path.GetFileNameWithoutExtension(safeName);
        var candidate = Path.Combine(directory, safeName);
        var suffix = 2;
        while (File.Exists(candidate) || File.Exists(Path.ChangeExtension(candidate, ".xmp")))
        {
            candidate = Path.Combine(directory, $"{stem}_{suffix}{extension}");
            suffix++;
        }

        return candidate;
    }

    private static string BuildXmp(
        PhotoExportCatalogItemDto item,
        MemoryKeeper.Application.DTOs.Gallery.PhotoDetailDto detail)
    {
        XNamespace x = "adobe:ns:meta/";
        XNamespace rdf = "http://www.w3.org/1999/02/22-rdf-syntax-ns#";
        XNamespace dc = "http://purl.org/dc/elements/1.1/";
        XNamespace xmp = "http://ns.adobe.com/xap/1.0/";
        XNamespace exif = "http://ns.adobe.com/exif/1.0/";
        XNamespace mk = "https://memorykeeper.local/ns/1.0/";
        XNamespace xml = XNamespace.Xml;

        var metadata = detail.Metadata;
        var capture = GetDate(metadata, "datetime_original") ?? item.CaptureDatetime;
        var latitude = GetDouble(metadata, "gps_lat") ?? item.Latitude;
        var longitude = GetDouble(metadata, "gps_lon") ?? item.Longitude;
        var country = GetString(metadata, "country") ?? item.Country;
        var province = GetString(metadata, "province");
        var city = GetString(metadata, "city") ?? item.Region;
        var district = GetString(metadata, "district");
        var rawAddress = GetString(metadata, "place_name") ?? detail.GeocodedPlaceName;
        var place = detail.PlaceDisplayName ?? detail.PlaceCanonicalName ?? item.Place;
        var tags = detail.Tags
            .Select(tag => string.IsNullOrWhiteSpace(tag.DisplayName) ? tag.Tag : tag.DisplayName)
            .Where(tag => !string.IsNullOrWhiteSpace(tag))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var description = new XElement(rdf + "Description",
            new XAttribute(rdf + "about", string.Empty),
            new XAttribute(XNamespace.Xmlns + "dc", dc),
            new XAttribute(XNamespace.Xmlns + "xmp", xmp),
            new XAttribute(XNamespace.Xmlns + "exif", exif),
            new XAttribute(XNamespace.Xmlns + "mk", mk),
            new XAttribute(mk + "favorite", detail.Favorite.ToString().ToLowerInvariant()),
            new XAttribute(mk + "country", country ?? string.Empty),
            new XAttribute(mk + "province", province ?? string.Empty),
            new XAttribute(mk + "city", city ?? string.Empty),
            new XAttribute(mk + "district", district ?? string.Empty),
            new XAttribute(mk + "place", place ?? string.Empty),
            new XAttribute(mk + "rawAddress", rawAddress ?? string.Empty));

        if (capture.HasValue)
        {
            description.Add(new XAttribute(xmp + "CreateDate", capture.Value.ToString("O", CultureInfo.InvariantCulture)));
        }

        if (latitude.HasValue)
        {
            description.Add(new XAttribute(exif + "GPSLatitude", latitude.Value.ToString("R", CultureInfo.InvariantCulture)));
        }

        if (longitude.HasValue)
        {
            description.Add(new XAttribute(exif + "GPSLongitude", longitude.Value.ToString("R", CultureInfo.InvariantCulture)));
        }

        if (tags.Count > 0)
        {
            description.Add(new XElement(dc + "subject",
                new XElement(rdf + "Bag", tags.Select(tag => new XElement(rdf + "li", tag)))));
        }

        if (!string.IsNullOrWhiteSpace(detail.Memo))
        {
            description.Add(new XElement(dc + "description",
                new XElement(rdf + "Alt",
                    new XElement(rdf + "li", new XAttribute(xml + "lang", "x-default"), detail.Memo))));
        }

        return new XDocument(
            new XDeclaration("1.0", "utf-8", null),
            new XElement(x + "xmpmeta",
                new XAttribute(XNamespace.Xmlns + "x", x),
                new XElement(rdf + "RDF", description))).ToString();
    }

    private static string SafeSegment(string? value, string fallback)
    {
        var trimmed = string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
        var invalid = Path.GetInvalidFileNameChars().ToHashSet();
        var chars = trimmed.Select(character =>
            invalid.Contains(character) || character is '/' or '\\' ? '_' : character).ToArray();
        var safe = new string(chars).Trim().TrimEnd('.');
        return string.IsNullOrWhiteSpace(safe) ? fallback : safe;
    }

    private static string? GetString(IReadOnlyDictionary<string, JsonElement> metadata, string key) =>
        metadata.TryGetValue(key, out var value) && value.ValueKind is not JsonValueKind.Null
            ? value.ValueKind == JsonValueKind.String ? value.GetString() : value.ToString()
            : null;

    private static double? GetDouble(IReadOnlyDictionary<string, JsonElement> metadata, string key)
    {
        var raw = GetString(metadata, key);
        return double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ? value : null;
    }

    private static DateTimeOffset? GetDate(IReadOnlyDictionary<string, JsonElement> metadata, string key)
    {
        var raw = GetString(metadata, key);
        return DateTimeOffset.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var value)
            ? value
            : null;
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // Best-effort cleanup of an incomplete export copy.
        }
    }
}
