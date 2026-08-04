using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using MemoryKeeper.Application.DTOs;
using MemoryKeeper.Domain.Entities;

namespace MemoryKeeper.Application;

/// <summary>
/// Normalizes Google location strings into stable Korean-friendly Canonical names (MK-042Q).
/// Osaka / Osaka-shi / 大阪市 → Canonical "오사카".
/// </summary>
public static partial class PlaceNormalizer
{
    public sealed record NormalizedLocation(
        string Country,
        string Province,
        string City,
        string DisplayName,
        string CanonicalName);

    private static readonly Dictionary<string, string> CountryAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["japan"] = "일본",
        ["jp"] = "일본",
        ["日本"] = "일본",
        ["にほん"] = "일본",
        ["일본"] = "일본",
        ["korea"] = "대한민국",
        ["south korea"] = "대한민국",
        ["republic of korea"] = "대한민국",
        ["kr"] = "대한민국",
        ["한국"] = "대한민국",
        ["대한민국"] = "대한민국",
        ["southkorea"] = "대한민국",
        ["china"] = "중국",
        ["cn"] = "중국",
        ["中国"] = "중국",
        ["중국"] = "중국",
        ["united states"] = "미국",
        ["united states of america"] = "미국",
        ["usa"] = "미국",
        ["us"] = "미국",
        ["america"] = "미국",
        ["미국"] = "미국",
        ["maldives"] = "몰디브",
        ["republic of maldives"] = "몰디브",
        ["mv"] = "몰디브",
        ["몰디브"] = "몰디브",
        ["thailand"] = "태국",
        ["th"] = "태국",
        ["태국"] = "태국",
        ["vietnam"] = "베트남",
        ["vn"] = "베트남",
        ["베트남"] = "베트남",
        ["france"] = "프랑스",
        ["fr"] = "프랑스",
        ["프랑스"] = "프랑스",
        ["italy"] = "이탈리아",
        ["it"] = "이탈리아",
        ["이탈리아"] = "이탈리아",
        ["spain"] = "스페인",
        ["es"] = "스페인",
        ["스페인"] = "스페인",
        ["united kingdom"] = "영국",
        ["uk"] = "영국",
        ["great britain"] = "영국",
        ["영국"] = "영국",
        ["taiwan"] = "대만",
        ["tw"] = "대만",
        ["대만"] = "대만",
        ["hong kong"] = "홍콩",
        ["hk"] = "홍콩",
        ["홍콩"] = "홍콩",
        ["singapore"] = "싱가포르",
        ["sg"] = "싱가포르",
        ["싱가포르"] = "싱가포르",
        ["indonesia"] = "인도네시아",
        ["id"] = "인도네시아",
        ["인도네시아"] = "인도네시아",
        ["australia"] = "호주",
        ["au"] = "호주",
        ["호주"] = "호주"
    };

    private static readonly Dictionary<string, string> PlaceAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["osaka"] = "오사카",
        ["osaka-shi"] = "오사카",
        ["osakashi"] = "오사카",
        ["osaka city"] = "오사카",
        ["osaka-fu"] = "오사카",
        ["大阪"] = "오사카",
        ["大阪市"] = "오사카",
        ["大阪府"] = "오사카",
        ["おおさか"] = "오사카",
        ["おおさかし"] = "오사카",
        ["오사카"] = "오사카",
        ["오사카시"] = "오사카",
        ["tokyo"] = "도쿄",
        ["tokyo-to"] = "도쿄",
        ["tokyo metropolis"] = "도쿄",
        ["東京"] = "도쿄",
        ["東京都"] = "도쿄",
        ["도쿄"] = "도쿄",
        ["도쿄도"] = "도쿄",
        ["kyoto"] = "교토",
        ["kyoto-shi"] = "교토",
        ["京都"] = "교토",
        ["京都市"] = "교토",
        ["교토"] = "교토",
        ["교토시"] = "교토",
        ["nara"] = "나라",
        ["nara-shi"] = "나라",
        ["奈良"] = "나라",
        ["奈良市"] = "나라",
        ["나라"] = "나라",
        ["나라시"] = "나라",
        ["fukuoka"] = "후쿠오카",
        ["fukuoka-shi"] = "후쿠오카",
        ["福岡"] = "후쿠오카",
        ["福岡市"] = "후쿠오카",
        ["후쿠오카"] = "후쿠오카",
        ["sapporo"] = "삿포로",
        ["札幌"] = "삿포로",
        ["札幌市"] = "삿포로",
        ["삿포로"] = "삿포로",
        ["yokohama"] = "요코하마",
        ["横浜"] = "요코하마",
        ["横浜市"] = "요코하마",
        ["요코하마"] = "요코하마",
        ["nagoya"] = "나고야",
        ["名古屋"] = "나고야",
        ["名古屋市"] = "나고야",
        ["나고야"] = "나고야",
        ["seoul"] = "서울",
        ["seoul-si"] = "서울",
        ["서울"] = "서울",
        ["서울시"] = "서울",
        ["서울특별시"] = "서울",
        ["busan"] = "부산",
        ["부산"] = "부산",
        ["부산시"] = "부산",
        ["부산광역시"] = "부산",
        ["incheon"] = "인천",
        ["인천"] = "인천",
        ["인천광역시"] = "인천",
        ["jeju"] = "제주",
        ["jeju-si"] = "제주",
        ["제주"] = "제주",
        ["제주시"] = "제주",
        ["제주특별자치도"] = "제주",
        ["male"] = "말레",
        ["malé"] = "말레",
        ["말레"] = "말레",
        ["beijing"] = "베이징",
        ["北京"] = "베이징",
        ["베이징"] = "베이징",
        ["shanghai"] = "상하이",
        ["上海"] = "상하이",
        ["상하이"] = "상하이",
        ["taipei"] = "타이베이",
        ["台北"] = "타이베이",
        ["타이베이"] = "타이베이",
        ["bangkok"] = "방콕",
        ["방콕"] = "방콕",
        ["paris"] = "파리",
        ["파리"] = "파리",
        ["rome"] = "로마",
        ["roma"] = "로마",
        ["로마"] = "로마",
        ["london"] = "런던",
        ["런던"] = "런던",
        ["new york"] = "뉴욕",
        ["new york city"] = "뉴욕",
        ["nyc"] = "뉴욕",
        ["뉴욕"] = "뉴욕",
        ["universal studios japan"] = "유니버설 스튜디오 재팬",
        ["universal studios"] = "유니버설 스튜디오 재팬",
        ["ユニバーサル・スタジオ・ジャパン"] = "유니버설 스튜디오 재팬",
        ["ユニバーサルスタジオジャパン"] = "유니버설 스튜디오 재팬",
        ["유니버설 스튜디오 재팬"] = "유니버설 스튜디오 재팬",
        ["유니버셜 스튜디오 재팬"] = "유니버설 스튜디오 재팬",
        ["ハテナブロック"] = "하테나 블록",
        ["hatena block"] = "하테나 블록",
        ["하테나 블록"] = "하테나 블록",
        ["하테나블록"] = "하테나 블록",
        ["konohana ward"] = "고노하나",
        ["konohana"] = "고노하나",
        ["此花区"] = "고노하나",
        ["此花"] = "고노하나",
        ["고노하나"] = "고노하나"
    };

    public static NormalizedLocation Normalize(LocationResult location)
    {
        ArgumentNullException.ThrowIfNull(location);

        var country = NormalizeCountry(location.Country);
        var province = NormalizeRegion(location.Province);
        var city = NormalizePlace(location.City);

        var rawDisplay = string.IsNullOrWhiteSpace(location.DisplayName)
            ? (!string.IsNullOrWhiteSpace(location.City)
                ? location.City
                : !string.IsNullOrWhiteSpace(location.Province)
                    ? location.Province
                    : location.Country)
            : location.DisplayName;

        var normalizedDisplay = NormalizePlace(rawDisplay);
        if (string.IsNullOrWhiteSpace(normalizedDisplay))
        {
            normalizedDisplay = !string.IsNullOrWhiteSpace(city)
                ? city
                : !string.IsNullOrWhiteSpace(province)
                    ? province
                    : !string.IsNullOrWhiteSpace(country)
                        ? country
                        : "Unknown Place";
        }

        // Prefer city alias as canonical when display is city-like; else use normalized display.
        var canonicalSeed = !string.IsNullOrWhiteSpace(city) && IsCityLikeName(rawDisplay, location.City)
            ? city
            : normalizedDisplay;

        var canonical = BuildCanonicalName(canonicalSeed);
        var displayName = PreferKoreanDisplay(normalizedDisplay, canonical);

        return new NormalizedLocation(
            Country: country,
            Province: string.IsNullOrWhiteSpace(province) ? canonical : province,
            City: string.IsNullOrWhiteSpace(city) ? canonical : city,
            DisplayName: displayName,
            CanonicalName: canonical);
    }

    public static string NormalizeCountry(string? value)
    {
        var trimmed = CollapseWhitespace(value);
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            return string.Empty;
        }

        if (CountryAliases.TryGetValue(trimmed, out var alias))
        {
            return alias;
        }

        var compact = CompactKey(trimmed);
        if (CountryAliases.TryGetValue(compact, out alias))
        {
            return alias;
        }

        return trimmed;
    }

    public static string NormalizeRegion(string? value) => NormalizePlace(value);

    public static string NormalizePlace(string? value)
    {
        var trimmed = CollapseWhitespace(value);
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            return string.Empty;
        }

        if (PlaceAliases.TryGetValue(trimmed, out var alias))
        {
            return alias;
        }

        var withoutSuffix = StripAdministrativeSuffixes(trimmed);
        if (PlaceAliases.TryGetValue(withoutSuffix, out alias))
        {
            return alias;
        }

        var compact = CompactKey(withoutSuffix);
        if (PlaceAliases.TryGetValue(compact, out alias))
        {
            return alias;
        }

        if (PlaceAliases.TryGetValue(CompactKey(trimmed), out alias))
        {
            return alias;
        }

        if (ContainsKana(withoutSuffix))
        {
            var transliterated = TransliterateKanaToHangul(withoutSuffix);
            if (!string.IsNullOrWhiteSpace(transliterated) && ContainsHangul(transliterated))
            {
                return CollapseWhitespace(transliterated);
            }
        }

        return withoutSuffix;
    }

    public static string BuildCanonicalName(string? placeName)
    {
        var normalized = NormalizePlace(placeName);
        return string.IsNullOrWhiteSpace(normalized) ? "Unknown Place" : normalized;
    }

    public static bool CanonicalEquals(string? left, string? right) =>
        string.Equals(
            BuildCanonicalName(left),
            BuildCanonicalName(right),
            StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// UI label for gallery / detail: prefer Korean DisplayName, alias, or CanonicalName.
    /// </summary>
    public static string GetDisplayLabel(Place place)
    {
        ArgumentNullException.ThrowIfNull(place);

        var display = place.DisplayName?.Trim() ?? string.Empty;
        var canonical = !string.IsNullOrWhiteSpace(place.CanonicalName)
            ? BuildCanonicalName(place.CanonicalName)
            : BuildCanonicalName(display);

        if (!string.IsNullOrWhiteSpace(display) && ContainsHangul(display))
        {
            return display;
        }

        if (!string.IsNullOrWhiteSpace(display) && !ContainsHangul(display))
        {
            var normalizedDisplay = NormalizePlace(display);
            if (HasKoreanLabel(normalizedDisplay))
            {
                return normalizedDisplay;
            }

            if (HasKoreanLabel(canonical))
            {
                return canonical;
            }

            return string.IsNullOrWhiteSpace(normalizedDisplay) ? display : normalizedDisplay;
        }

        if (!string.IsNullOrWhiteSpace(display))
        {
            return display;
        }

        if (HasKoreanLabel(canonical))
        {
            return canonical;
        }

        var cityLabel = ResolveCityLabel(place, string.Empty);
        return !string.IsNullOrWhiteSpace(cityLabel) ? cityLabel : canonical;
    }

    /// <summary>
    /// City node label: city → province → canonical when ward-level names stay in Japanese.
    /// </summary>
    public static string ResolveCityLabel(Place place, string fallback = "기타")
    {
        ArgumentNullException.ThrowIfNull(place);

        foreach (var candidate in new[] { place.City, place.Province, place.CanonicalName, place.DisplayName })
        {
            if (string.IsNullOrWhiteSpace(candidate))
            {
                continue;
            }

            var normalized = NormalizePlace(candidate);
            if (HasKoreanLabel(normalized))
            {
                return normalized;
            }
        }

        var city = NormalizePlace(place.City);
        return string.IsNullOrWhiteSpace(city) ? fallback : city;
    }

    /// <summary>
    /// True when Google Place Details can supply a Korean display name.
    /// </summary>
    public static bool NeedsKoreanLabelRefresh(Place place)
    {
        ArgumentNullException.ThrowIfNull(place);

        if (string.IsNullOrWhiteSpace(place.GooglePlaceId))
        {
            return false;
        }

        return !HasKoreanLabel(GetDisplayLabel(place));
    }

    /// <summary>
    /// True when query matches country, region, place name, canonical name, or known aliases.
    /// </summary>
    public static bool MatchesSearch(Place place, string query)
    {
        ArgumentNullException.ThrowIfNull(place);
        ArgumentException.ThrowIfNullOrWhiteSpace(query);

        var q = query.Trim();
        var candidates = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        void Add(string? value)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                candidates.Add(value.Trim());
            }
        }

        Add(place.Country);
        Add(NormalizeCountry(place.Country));
        Add(place.Province);
        Add(NormalizeRegion(place.Province));
        Add(place.City);
        Add(NormalizePlace(place.City));
        Add(place.DisplayName);
        Add(NormalizePlace(place.DisplayName));
        Add(place.CanonicalName);
        Add(BuildCanonicalName(place.CanonicalName ?? place.DisplayName));
        Add(GetDisplayLabel(place));
        Add(ResolveCityLabel(place, string.Empty));
        Add(place.Address);

        foreach (var candidate in candidates)
        {
            if (candidate.Contains(q, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        if (PlaceAliases.TryGetValue(q, out var aliasTarget)
            || PlaceAliases.TryGetValue(CompactKey(q), out aliasTarget))
        {
            foreach (var candidate in candidates)
            {
                if (candidate.Contains(aliasTarget, StringComparison.OrdinalIgnoreCase)
                    || aliasTarget.Contains(candidate, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
        }

        foreach (var alias in PlaceAliases)
        {
            if (!alias.Key.Contains(q, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            foreach (var candidate in candidates)
            {
                if (candidate.Contains(alias.Value, StringComparison.OrdinalIgnoreCase)
                    || alias.Value.Contains(candidate, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static string PreferKoreanDisplay(string normalizedDisplay, string canonical)
    {
        if (!string.IsNullOrWhiteSpace(canonical)
            && ContainsHangul(canonical)
            && !ContainsHangul(normalizedDisplay))
        {
            return canonical;
        }

        return normalizedDisplay;
    }

    private static bool IsCityLikeName(string? displayName, string? city)
    {
        if (string.IsNullOrWhiteSpace(displayName))
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(city))
        {
            return false;
        }

        return CanonicalEquals(displayName, city)
               || string.Equals(CollapseWhitespace(displayName), CollapseWhitespace(city), StringComparison.OrdinalIgnoreCase);
    }

    private static string StripAdministrativeSuffixes(string value)
    {
        var current = value.Trim();
        // Multi-pass for stacked suffixes (e.g. 서울특별시).
        for (var i = 0; i < 3; i++)
        {
            var next = SuffixRegex().Replace(current, string.Empty).Trim().Trim('-', ' ');
            if (string.Equals(next, current, StringComparison.Ordinal))
            {
                break;
            }

            current = next;
        }

        return string.IsNullOrWhiteSpace(current) ? value.Trim() : current;
    }

    private static string CollapseWhitespace(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(value.Trim().Length);
        var previousSpace = false;
        foreach (var ch in value.Trim())
        {
            if (char.IsWhiteSpace(ch))
            {
                if (!previousSpace)
                {
                    builder.Append(' ');
                    previousSpace = true;
                }
            }
            else
            {
                builder.Append(ch);
                previousSpace = false;
            }
        }

        return builder.ToString();
    }

    private static string CompactKey(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (var ch in value.Normalize(NormalizationForm.FormKC))
        {
            if (char.IsLetterOrDigit(ch))
            {
                builder.Append(char.ToLower(ch, CultureInfo.InvariantCulture));
            }
        }

        return builder.ToString();
    }

    private static bool HasKoreanLabel(string value) =>
        !string.IsNullOrWhiteSpace(value)
        && (ContainsHangul(value) || PlaceAliases.ContainsValue(value));

    private static bool ContainsHangul(string value) =>
        value.Any(ch => ch is >= '\uAC00' and <= '\uD7A3');

    private static bool ContainsKana(string value) =>
        value.Any(ch => ch is (>= '\u3040' and <= '\u309F') or (>= '\u30A0' and <= '\u30FF'));

    /// <summary>
    /// Approximate katakana/hiragana → Hangul for gallery labels when Google has no Korean name.
    /// </summary>
    private static string TransliterateKanaToHangul(string value)
    {
        var normalized = value.Normalize(NormalizationForm.FormKC);
        var builder = new StringBuilder(normalized.Length * 2);

        for (var i = 0; i < normalized.Length; i++)
        {
            var ch = normalized[i];
            if (ch is '・' or 'ｰ' or '-' or ' ')
            {
                if (builder.Length > 0 && builder[^1] != ' ')
                {
                    builder.Append(' ');
                }

                continue;
            }

            if (ch is 'ー' or '\u30FC')
            {
                continue; // long vowel mark: omit for Korean loanword style
            }

            if (ch is 'ッ' or 'っ')
            {
                // Geminate: double next consonant by skipping; Korean often just tightens next syllable.
                continue;
            }

            // Youon: base + small ya/yu/yo
            if (i + 1 < normalized.Length && IsSmallY(normalized[i + 1]))
            {
                var combo = ToHangulYouon(ch, normalized[i + 1]);
                if (combo is not null)
                {
                    builder.Append(combo);
                    i++;
                    continue;
                }
            }

            if (KanaHangul.TryGetValue(ch, out var hangul))
            {
                builder.Append(hangul);
                continue;
            }

            // Hiragana → katakana then map
            if (ch is >= '\u3041' and <= '\u3096')
            {
                var kata = (char)(ch + 0x60);
                if (KanaHangul.TryGetValue(kata, out hangul))
                {
                    builder.Append(hangul);
                    continue;
                }
            }

            builder.Append(ch);
        }

        return builder.ToString().Trim();
    }

    private static bool IsSmallY(char ch) => ch is 'ャ' or 'ュ' or 'ョ' or 'ゃ' or 'ゅ' or 'ょ';

    private static string? ToHangulYouon(char baseKana, char smallY)
    {
        var y = smallY switch
        {
            'ャ' or 'ゃ' => "야",
            'ュ' or 'ゅ' => "유",
            'ョ' or 'ょ' => "요",
            _ => null
        };
        if (y is null)
        {
            return null;
        }

        // Common youon bases used in place names.
        return baseKana switch
        {
            'キ' or 'き' => y switch { "야" => "캬", "유" => "큐", "요" => "쿄", _ => null },
            'シ' or 'し' => y switch { "야" => "샤", "유" => "슈", "요" => "쇼", _ => null },
            'チ' or 'ち' => y switch { "야" => "챠", "유" => "츄", "요" => "쵸", _ => null },
            'ニ' or 'に' => y switch { "야" => "냐", "유" => "뉴", "요" => "뇨", _ => null },
            'ヒ' or 'ひ' => y switch { "야" => "햐", "유" => "휴", "요" => "효", _ => null },
            'ミ' or 'み' => y switch { "야" => "먀", "유" => "뮤", "요" => "묘", _ => null },
            'リ' or 'り' => y switch { "야" => "랴", "유" => "류", "요" => "료", _ => null },
            'ギ' or 'ぎ' => y switch { "야" => "갸", "유" => "규", "요" => "교", _ => null },
            'ジ' or 'じ' => y switch { "야" => "자", "유" => "주", "요" => "조", _ => null },
            'ビ' or 'び' => y switch { "야" => "뱌", "유" => "뷰", "요" => "뵤", _ => null },
            'ピ' or 'ぴ' => y switch { "야" => "퍄", "유" => "퓨", "요" => "표", _ => null },
            _ => null
        };
    }

    private static readonly Dictionary<char, string> KanaHangul = new()
    {
        ['ア'] = "아", ['イ'] = "이", ['ウ'] = "우", ['エ'] = "에", ['オ'] = "오",
        ['カ'] = "카", ['キ'] = "키", ['ク'] = "쿠", ['ケ'] = "케", ['コ'] = "코",
        ['サ'] = "사", ['シ'] = "시", ['ス'] = "스", ['セ'] = "세", ['ソ'] = "소",
        ['タ'] = "타", ['チ'] = "치", ['ツ'] = "츠", ['テ'] = "테", ['ト'] = "토",
        ['ナ'] = "나", ['ニ'] = "니", ['ヌ'] = "누", ['ネ'] = "네", ['ノ'] = "노",
        ['ハ'] = "하", ['ヒ'] = "히", ['フ'] = "후", ['ヘ'] = "헤", ['ホ'] = "호",
        ['マ'] = "마", ['ミ'] = "미", ['ム'] = "무", ['メ'] = "메", ['モ'] = "모",
        ['ヤ'] = "야", ['ユ'] = "유", ['ヨ'] = "요",
        ['ラ'] = "라", ['リ'] = "리", ['ル'] = "루", ['レ'] = "레", ['ロ'] = "로",
        ['ワ'] = "와", ['ヲ'] = "오", ['ン'] = "ㄴ",
        ['ガ'] = "가", ['ギ'] = "기", ['グ'] = "구", ['ゲ'] = "게", ['ゴ'] = "고",
        ['ザ'] = "자", ['ジ'] = "지", ['ズ'] = "즈", ['ゼ'] = "제", ['ゾ'] = "조",
        ['ダ'] = "다", ['ヂ'] = "지", ['ヅ'] = "즈", ['デ'] = "데", ['ド'] = "도",
        ['バ'] = "바", ['ビ'] = "비", ['ブ'] = "브", ['ベ'] = "베", ['ボ'] = "보",
        ['パ'] = "파", ['ピ'] = "피", ['プ'] = "프", ['ペ'] = "페", ['ポ'] = "포",
        ['ヴ'] = "브"
    };

    [GeneratedRegex(
        @"(-shi|-ku|-cho|-machi|-gun|-ken|-fu|-to|-si|-gu|-do|市|区|町|村|郡|県|府|都|特別市|広域市|특별시|광역시|특별자치도|시|구|군|도|City|Prefecture)$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SuffixRegex();
}
