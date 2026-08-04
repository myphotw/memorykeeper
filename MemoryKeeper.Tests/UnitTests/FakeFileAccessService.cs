using MemoryKeeper.Application.Interfaces;
using MemoryKeeper.Infrastructure.Storage;
using Microsoft.Extensions.Logging.Abstractions;

namespace MemoryKeeper.Tests.UnitTests;

/// <summary>
/// Thin wrapper around LocalFileAccessService for unit tests.
/// </summary>
internal sealed class FakeFileAccessService : IFileAccessService
{
    private readonly LocalFileAccessService _inner = new(NullLogger<LocalFileAccessService>.Instance);

    public string ResolveAbsolutePath(string photoRoot, string relativePath) =>
        _inner.ResolveAbsolutePath(photoRoot, relativePath);

    public bool PhotoRootExists(string photoRoot) => _inner.PhotoRootExists(photoRoot);

    public bool FileExists(string absolutePath) => _inner.FileExists(absolutePath);

    public Task<Stream> OpenReadAsync(string absolutePath, CancellationToken cancellationToken = default) =>
        _inner.OpenReadAsync(absolutePath, cancellationToken);

    public string ToRelativePath(string path, string? photoRoot = null) =>
        _inner.ToRelativePath(path, photoRoot);
}
