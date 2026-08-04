namespace MemoryKeeper.Application;

/// <summary>
/// Validates Google Maps / Places API key format before save or network use.
/// </summary>
public static class GoogleMapsApiKeyValidator
{
    public static bool LooksValid(string? apiKey)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return false;
        }

        var key = apiKey.Trim();
        if (key.Any(char.IsWhiteSpace))
        {
            return false;
        }

        // Google API keys typically start with "AIza" and are ~39 chars.
        if (!key.StartsWith("AIza", StringComparison.Ordinal))
        {
            return false;
        }

        return key.Length is >= 30 and <= 128;
    }

    public static string? NormalizeOrNull(string? apiKey)
    {
        var key = apiKey?.Trim();
        return LooksValid(key) ? key : null;
    }

    public static void EnsureValidOrEmpty(string? apiKey)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return;
        }

        if (!LooksValid(apiKey))
        {
            throw new InvalidOperationException(
                "Google API Key 형식이 올바르지 않습니다. Google Cloud Console에서 발급한 Key(AIza…)를 입력하세요. Maps JavaScript / Geocoding / Places(레거시) API를 활성화해야 합니다.");
        }
    }
}
