using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;

namespace MemoryKeeper.Application;

/// <summary>
/// Maps TC-Backend gallery <c>file_id</c> (often a SHA-256 hex digest) to a stable <see cref="Guid"/>
/// for UI/navigation while preserving the original string for API paths.
/// </summary>
public static class BackendFileIdCodec
{
    private static readonly ConcurrentDictionary<Guid, string> OriginalByGuid = new();

    public static Guid ToGuid(string? fileId)
    {
        if (string.IsNullOrWhiteSpace(fileId))
        {
            return Guid.Empty;
        }

        var trimmed = fileId.Trim();
        if (Guid.TryParse(trimmed, out var parsed))
        {
            OriginalByGuid[parsed] = trimmed;
            return parsed;
        }

        var guid = CreateDeterministicGuid(trimmed);
        OriginalByGuid[guid] = trimmed;
        return guid;
    }

    /// <summary>
    /// Returns the original Backend <c>file_id</c> for API calls, or a Guid "D" form as fallback.
    /// </summary>
    public static string ToApiFileId(Guid id)
    {
        if (id == Guid.Empty)
        {
            return string.Empty;
        }

        return OriginalByGuid.TryGetValue(id, out var original)
            ? original
            : id.ToString("D");
    }

    public static void Remember(Guid id, string fileId)
    {
        if (id == Guid.Empty || string.IsNullOrWhiteSpace(fileId))
        {
            return;
        }

        OriginalByGuid[id] = fileId.Trim();
    }

    private static Guid CreateDeterministicGuid(string fileId)
    {
        var normalized = fileId.ToLowerInvariant();
        Span<byte> bytes = stackalloc byte[16];

        if (IsHex(normalized) && normalized.Length >= 32)
        {
            Convert.FromHexString(normalized.AsSpan(0, 32)).CopyTo(bytes);
        }
        else
        {
            SHA256.HashData(Encoding.UTF8.GetBytes(normalized)).AsSpan(0, 16).CopyTo(bytes);
        }

        return new Guid(bytes);
    }

    private static bool IsHex(string value)
    {
        foreach (var c in value)
        {
            var isDigit = c is >= '0' and <= '9';
            var isHexLetter = c is >= 'a' and <= 'f';
            if (!isDigit && !isHexLetter)
            {
                return false;
            }
        }

        return value.Length > 0;
    }
}
