using MemoryKeeper.Mobile.Diagnostics;

namespace MemoryKeeper.Tests.UnitTests;

public sealed class MobileOriginalReadProbeTests
{
    [Fact]
    public void ReadableStream_RecordsOpenReadabilityAndOneByte_ThenClosesStream()
    {
        var stream = new MemoryStream([0x42, 0x43]);
        var records = new List<(MobileDiagnosticStage Stage, MobileDiagnosticResult Result, string? ExceptionType)>();

        MobileOriginalReadProbe.Probe(() => stream,
            (stage, result, exception) => records.Add((stage, result, exception?.GetType().Name)));

        var expected = new List<(MobileDiagnosticStage Stage, MobileDiagnosticResult Result, string? ExceptionType)>
        {
            (MobileDiagnosticStage.OpenInputStream, MobileDiagnosticResult.Succeeded, (string?)null),
            (MobileDiagnosticStage.CanRead, MobileDiagnosticResult.Succeeded, (string?)null),
            (MobileDiagnosticStage.ReadByte, MobileDiagnosticResult.Succeeded, (string?)null),
        };
        Assert.Equal(expected, records);
        Assert.Throws<ObjectDisposedException>(() => stream.ReadByte());
    }

    [Fact]
    public void OpenSecurityFailure_RecordsExceptionTypeAndDoesNotThrow()
    {
        var records = new List<(MobileDiagnosticStage Stage, MobileDiagnosticResult Result, string? ExceptionType)>();

        MobileOriginalReadProbe.Probe(
            () => throw new System.Security.SecurityException("private URI must not be logged"),
            (stage, result, exception) => records.Add((stage, result, exception?.GetType().Name)));

        var expected = new List<(MobileDiagnosticStage Stage, MobileDiagnosticResult Result, string? ExceptionType)>
        {
            (MobileDiagnosticStage.OpenInputStream, MobileDiagnosticResult.Failed, "SecurityException"),
            (MobileDiagnosticStage.CanRead, MobileDiagnosticResult.NotApplicable, (string?)null),
            (MobileDiagnosticStage.ReadByte, MobileDiagnosticResult.NotApplicable, (string?)null),
        };
        Assert.Equal(expected, records);
    }

    [Fact]
    public void EmptyOrUnreadableStream_DoesNotClaimThatBytesWereRead()
    {
        var emptyRecords = new List<(MobileDiagnosticStage, MobileDiagnosticResult)>();
        MobileOriginalReadProbe.Probe(() => new MemoryStream(),
            (stage, result, _) => emptyRecords.Add((stage, result)));
        Assert.Equal((MobileDiagnosticStage.ReadByte, MobileDiagnosticResult.EndOfStream), emptyRecords[^1]);

        var unreadableRecords = new List<(MobileDiagnosticStage, MobileDiagnosticResult)>();
        MobileOriginalReadProbe.Probe(() => new UnreadableStream(),
            (stage, result, _) => unreadableRecords.Add((stage, result)));
        Assert.Equal((MobileDiagnosticStage.CanRead, MobileDiagnosticResult.Failed), unreadableRecords[^2]);
        Assert.Equal((MobileDiagnosticStage.ReadByte, MobileDiagnosticResult.NotApplicable), unreadableRecords[^1]);
    }

    [Fact]
    public void LoggingFailure_DoesNotEscapeAndStreamIsClosed()
    {
        var stream = new MemoryStream([0x42]);
        MobileOriginalReadProbe.Probe(() => stream, (_, _, _) => throw new IOException("log unavailable"));
        Assert.Throws<ObjectDisposedException>(() => stream.ReadByte());
    }

    private sealed class UnreadableStream : Stream
    {
        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override void Flush() { }
    }
}
