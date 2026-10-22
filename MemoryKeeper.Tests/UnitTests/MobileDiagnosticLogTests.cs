using MemoryKeeper.Mobile.Diagnostics;

namespace MemoryKeeper.Tests.UnitTests;

public sealed class MobileDiagnosticLogTests
{
    [Fact]
    public void Write_PersistsOnlyCodesAndExceptionTypeAcrossInstances()
    {
        var root = CreateRoot();
        try
        {
            var log = new MobileDiagnosticLog(root);
            var secret = "secret-token-and-private-path";
            log.Write(MobileDiagnosticEventCode.OriginalRead,
                MobileDiagnosticStage.OpenInputStream,
                MobileDiagnosticResult.Failed,
                new IOException(secret));

            var reopened = new MobileDiagnosticLog(root);
            var snapshot = reopened.CreateShareSnapshot(Path.Combine(root, "cache"));
            Assert.NotNull(snapshot);
            var line = Assert.Single(File.ReadAllLines(snapshot));
            var fields = line.Split('|');
            Assert.Equal(5, fields.Length);
            Assert.True(DateTimeOffset.TryParse(fields[0], out _));
            Assert.Equal("OriginalRead", fields[1]);
            Assert.Equal("OpenInputStream", fields[2]);
            Assert.Equal("Failed", fields[3]);
            Assert.Equal("IOException", fields[4]);
            Assert.DoesNotContain(secret, line, StringComparison.Ordinal);
            Assert.DoesNotContain(root, line, StringComparison.Ordinal);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void Write_RotatesToOneBoundedArchive_AndUsesOneReplaceableShareSnapshot()
    {
        var root = CreateRoot();
        try
        {
            var log = new MobileDiagnosticLog(root);
            for (var index = 0; index < 4000; index++)
            {
                log.Write(MobileDiagnosticEventCode.OriginalRead,
                    MobileDiagnosticStage.Staging,
                    MobileDiagnosticResult.Succeeded);
            }

            var directory = Path.Combine(root, "diagnostics");
            var files = Directory.GetFiles(directory);
            Assert.InRange(files.Length, 1, 2);
            Assert.All(files, file => Assert.InRange(new FileInfo(file).Length, 1, MobileDiagnosticLog.MaxFileBytes));
            var snapshot = log.CreateShareSnapshot(Path.Combine(root, "cache"));
            Assert.NotNull(snapshot);
            Assert.InRange(new FileInfo(snapshot).Length, 1, 2 * MobileDiagnosticLog.MaxFileBytes);
            Assert.Equal(snapshot, log.CreateShareSnapshot(Path.Combine(root, "cache")));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void NoLog_ReturnsNoShareFile_AndWriteFailureNeverEscapes()
    {
        var root = CreateRoot();
        try
        {
            var log = new MobileDiagnosticLog(root);
            Assert.Null(log.CreateShareSnapshot(Path.Combine(root, "cache")));
            var blockedRoot = Path.Combine(root, "blocked");
            File.WriteAllText(blockedRoot, "not a directory");
            var blocked = new MobileDiagnosticLog(blockedRoot);
            blocked.Write(MobileDiagnosticEventCode.OriginalRead,
                MobileDiagnosticStage.Staging,
                MobileDiagnosticResult.Failed);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    private static string CreateRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "MemoryKeeperDiagnosticTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }
}
