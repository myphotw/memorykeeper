namespace MemoryKeeper.Mobile.Diagnostics;

// A read-only, single-byte probe. Neither the stream nor its contents leave this method.
public static class MobileOriginalReadProbe
{
    public static void Probe(
        Func<Stream?> openStream,
        Action<MobileDiagnosticStage, MobileDiagnosticResult, Exception?> record)
    {
        Stream? stream = null;
        try
        {
            stream = openStream();
            if (stream is null)
            {
                SafeRecord(MobileDiagnosticStage.OpenInputStream, MobileDiagnosticResult.Failed);
                SafeRecord(MobileDiagnosticStage.CanRead, MobileDiagnosticResult.NotApplicable);
                SafeRecord(MobileDiagnosticStage.ReadByte, MobileDiagnosticResult.NotApplicable);
                return;
            }

            SafeRecord(MobileDiagnosticStage.OpenInputStream, MobileDiagnosticResult.Succeeded);
            bool canRead;
            try
            {
                canRead = stream.CanRead;
            }
            catch (Exception ex)
            {
                SafeRecord(MobileDiagnosticStage.CanRead, MobileDiagnosticResult.Failed, ex);
                SafeRecord(MobileDiagnosticStage.ReadByte, MobileDiagnosticResult.NotApplicable);
                return;
            }

            SafeRecord(MobileDiagnosticStage.CanRead,
                canRead ? MobileDiagnosticResult.Succeeded : MobileDiagnosticResult.Failed);
            if (!canRead)
            {
                SafeRecord(MobileDiagnosticStage.ReadByte, MobileDiagnosticResult.NotApplicable);
                return;
            }

            try
            {
                SafeRecord(MobileDiagnosticStage.ReadByte,
                    stream.ReadByte() >= 0 ? MobileDiagnosticResult.Succeeded : MobileDiagnosticResult.EndOfStream);
            }
            catch (Exception ex)
            {
                SafeRecord(MobileDiagnosticStage.ReadByte, MobileDiagnosticResult.Failed, ex);
            }
        }
        catch (Exception ex)
        {
            SafeRecord(MobileDiagnosticStage.OpenInputStream, MobileDiagnosticResult.Failed, ex);
            SafeRecord(MobileDiagnosticStage.CanRead, MobileDiagnosticResult.NotApplicable);
            SafeRecord(MobileDiagnosticStage.ReadByte, MobileDiagnosticResult.NotApplicable);
        }
        finally
        {
            try { stream?.Dispose(); }
            catch { /* Diagnostic cleanup cannot change the upload result. */ }
        }

        void SafeRecord(MobileDiagnosticStage stage, MobileDiagnosticResult result, Exception? exception = null)
        {
            try { record(stage, result, exception); }
            catch { /* Diagnostics cannot change the upload result. */ }
        }
    }
}
