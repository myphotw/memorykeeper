namespace MemoryKeeper.App.Views;

/// <summary>UI-only country → flag emoji (no ViewModel/DTO change).</summary>
internal static class TravelCountryEmoji
{
    public static string For(string? country)
    {
        if (string.IsNullOrWhiteSpace(country))
        {
            return "📍";
        }

        var key = country.Trim();
        return key switch
        {
            "일본" or "Japan" or "JP" => "🇯🇵",
            "한국" or "대한민국" or "Korea" or "South Korea" or "KR" => "🇰🇷",
            "미국" or "United States" or "USA" or "US" => "🇺🇸",
            "중국" or "China" or "CN" => "🇨🇳",
            "대만" or "Taiwan" or "TW" => "🇹🇼",
            "홍콩" or "Hong Kong" or "HK" => "🇭🇰",
            "태국" or "Thailand" or "TH" => "🇹🇭",
            "베트남" or "Vietnam" or "VN" => "🇻🇳",
            "싱가포르" or "Singapore" or "SG" => "🇸🇬",
            "프랑스" or "France" or "FR" => "🇫🇷",
            "이탈리아" or "Italy" or "IT" => "🇮🇹",
            "스페인" or "Spain" or "ES" => "🇪🇸",
            "영국" or "United Kingdom" or "UK" or "GB" => "🇬🇧",
            "독일" or "Germany" or "DE" => "🇩🇪",
            "호주" or "Australia" or "AU" => "🇦🇺",
            "캐나다" or "Canada" or "CA" => "🇨🇦",
            "필리핀" or "Philippines" or "PH" => "🇵🇭",
            "인도네시아" or "Indonesia" or "ID" => "🇮🇩",
            "말레이시아" or "Malaysia" or "MY" => "🇲🇾",
            "몰디브" or "Maldives" or "MV" => "🇲🇻",
            _ => "✈️"
        };
    }
}
