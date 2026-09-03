using System.Text.Json;
using MemoryKeeper.Application;
using MemoryKeeper.Application.DTOs;
using MemoryKeeper.App.Services;
using MemoryKeeper.Domain.Enums;
using BackendDetail = MemoryKeeper.Application.DTOs.Gallery.PhotoDetailDto;
using BackendPhoto = MemoryKeeper.Application.DTOs.Gallery.PhotoDto;

namespace MemoryKeeper.Tests.UnitTests;

public sealed class MediaTypeResolverTests
{
    [Theory]
    [InlineData("video/mp4", ".jpg", "photo.jpg", MediaType.Video)]
    [InlineData("image/jpeg", ".mp4", "video.mp4", MediaType.Photo)]
    [InlineData(" VIDEO/QUICKTIME ", null, null, MediaType.Video)]
    [InlineData(null, ".mp4", null, MediaType.Video)]
    [InlineData(null, ".MOV", null, MediaType.Video)]
    [InlineData(null, "mov", null, MediaType.Video)]
    [InlineData(null, null, "clip.MP4", MediaType.Video)]
    [InlineData(null, " ", "clip.mov", MediaType.Video)]
    [InlineData("application/octet-stream", ".mkv", null, MediaType.Video)]
    [InlineData(null, ".jpg", "clip.mp4", MediaType.Photo)]
    [InlineData(null, null, null, MediaType.Photo)]
    [InlineData("unknown", ".unknown", "unknown", MediaType.Photo)]
    public void ClassifiesWithMimeThenExtensionThenFilename(string? mime, string? extension, string? filename, MediaType expected) =>
        Assert.Equal(expected, MediaTypeResolver.Resolve(mime, extension, filename));

    [Theory]
    [InlineData("{}", null, null)]
    [InlineData("{\"extension\":null,\"mime_type\":null}", null, null)]
    [InlineData("{\"extension\":\".mp4\",\"mime_type\":\"video/mp4\"}", ".mp4", "video/mp4")]
    public void BothGalleryContractsAcceptOldAndNewJson(string json, string? extension, string? mime)
    {
        var fast = JsonSerializer.Deserialize<FastGalleryPhotoDto>(json)!;
        var common = JsonSerializer.Deserialize<BackendPhoto>(json)!;
        Assert.Equal(extension, fast.Extension);
        Assert.Equal(mime, fast.MimeType);
        Assert.Equal(extension, common.Extension);
        Assert.Equal(mime, common.MimeType);
    }

    [Theory]
    [InlineData("video/mp4", ".mp4", MediaType.Video)]
    [InlineData("image/jpeg", ".jpg", MediaType.Photo)]
    [InlineData(null, null, MediaType.Photo)]
    public void DetailMapperPreservesOriginalAndMediaMetadata(string? mime, string? extension, MediaType expected)
    {
        var result = GalleryBackendMapper.ToPhotoDetail(new BackendDetail
        {
            FileId = "000000000000002a", Filename = "item" + extension,
            MimeType = mime, Extension = extension,
            OriginalUrl = "/api/common/gallery/42/original",
            PreviewUrl = "/api/common/gallery/42/preview",
        }, "https://backend.test");
        Assert.Equal(expected, result.MediaType);
        Assert.Equal(extension, result.Extension);
        Assert.Equal(mime, result.MimeType);
        Assert.Equal("https://backend.test/api/common/gallery/42/original", result.OriginalPath);
        Assert.Equal("https://backend.test/api/common/gallery/42/preview", result.AbsoluteLibraryPath);
    }

    [Fact]
    public void GalleryMapperUsesFilenameFallbackAndKeepsJpegThumbnail()
    {
        var result = GalleryBackendMapper.ToGalleryMedia(new BackendPhoto
        {
            FileId = "000000000000002a", Filename = "clip.MOV", ThumbnailUrl = "/thumb.jpg",
        }, "https://backend.test");
        Assert.Equal(MediaType.Video, result.MediaType);
        Assert.Equal("https://backend.test/thumb.jpg", result.ThumbnailUrl);
    }
}
