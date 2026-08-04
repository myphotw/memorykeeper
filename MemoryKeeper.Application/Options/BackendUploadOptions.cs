namespace MemoryKeeper.Application.Options;

/// <summary>
/// Import-related flags from the <c>TcBackend</c> appsettings section.
/// </summary>
public sealed class BackendUploadOptions
{
    public const string SectionName = "TcBackend";

    /// <summary>
    /// When true, <see cref="Services.MediaImportService"/> uploads via <c>IUploadApiRepository</c>
    /// instead of the local SQLite import pipeline. Default false — existing Import preserved.
    /// </summary>
    public bool UseBackendUpload { get; set; }
}
