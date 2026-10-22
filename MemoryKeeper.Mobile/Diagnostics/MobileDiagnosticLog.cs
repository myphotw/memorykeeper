using System.Text;

namespace MemoryKeeper.Mobile.Diagnostics;

// Only these fixed codes and an exception type may leave the upload flow for disk.
public enum MobileDiagnosticEventCode
{
    PickerSelected, Selection, UriMetadata, OriginalRead, StagingComplete,
    MediaStoreReadProbe, OriginalReadProbe, DocumentReadProbe,
}

public enum MobileDiagnosticStage
{
    Picker, UriKind, FileName, DocumentReadGrant, MediaStoreConversion,
    MediaLocationPermission, RequireOriginal, OpenInputStream, Staging, Complete,
    CanRead, ReadByte,
}

public enum MobileDiagnosticResult
{
    PickImages, OpenDocument, GetContent, Cancelled, Succeeded, Failed, Granted, Denied,
    NotApplicable, Unsupported, PhotoPicker, Document, Media, Other,
    EndOfStream,
}

public sealed class MobileDiagnosticLog
{
    public const int MaxFileBytes = 128 * 1024;
    private const string ActiveName = "upload-diagnostics.log";
    private const string ArchiveName = "upload-diagnostics.previous.log";
    private readonly string _directory;
    private readonly object _gate = new();

    public MobileDiagnosticLog(string appDataDirectory) =>
        _directory = Path.Combine(appDataDirectory, "diagnostics");

    public static MobileDiagnosticLog? Current { get; private set; }

    public static void Initialize(string appDataDirectory)
    {
        try { Current = new MobileDiagnosticLog(appDataDirectory); }
        catch { /* Diagnostics must never prevent application startup. */ }
    }

    public void Write(
        MobileDiagnosticEventCode eventCode,
        MobileDiagnosticStage stage,
        MobileDiagnosticResult result,
        Exception? exception = null)
    {
        try
        {
            var exceptionType = exception?.GetType().Name ?? "None";
            if (exceptionType.Length > 64 || exceptionType.Any(ch => !char.IsAsciiLetterOrDigit(ch)))
            {
                exceptionType = "Exception";
            }

            var line = $"{DateTimeOffset.UtcNow:O}|{eventCode}|{stage}|{result}|{exceptionType}{Environment.NewLine}";
            var bytes = Encoding.UTF8.GetBytes(line);
            lock (_gate)
            {
                Directory.CreateDirectory(_directory);
                var active = Path.Combine(_directory, ActiveName);
                var archive = Path.Combine(_directory, ArchiveName);
                if (File.Exists(active) && new FileInfo(active).Length + bytes.Length > MaxFileBytes)
                {
                    File.Move(active, archive, overwrite: true);
                }

                using var stream = new FileStream(active, FileMode.Append, FileAccess.Write, FileShare.Read);
                stream.Write(bytes);
            }
        }
        catch
        {
            // Logging is best effort; in particular, an out-of-space device can still upload.
        }
    }

    public string? CreateShareSnapshot(string cacheDirectory)
    {
        lock (_gate)
        {
            var active = Path.Combine(_directory, ActiveName);
            var archive = Path.Combine(_directory, ArchiveName);
            if (!File.Exists(active) && !File.Exists(archive))
            {
                return null;
            }

            // A single, replaceable cache snapshot is exposed through MAUI's FileProvider.
            var sharingDirectory = Path.Combine(cacheDirectory, "sharing-root");
            Directory.CreateDirectory(sharingDirectory);
            var snapshot = Path.Combine(sharingDirectory, "MemoryKeeper-diagnostics.txt");
            using var output = new FileStream(snapshot, FileMode.Create, FileAccess.Write, FileShare.Read);
            foreach (var source in new[] { archive, active })
            {
                if (!File.Exists(source)) continue;
                using var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                input.CopyTo(output);
            }

            return snapshot;
        }
    }
}
