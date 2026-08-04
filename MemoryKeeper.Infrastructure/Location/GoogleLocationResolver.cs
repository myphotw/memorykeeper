using System.Net.Http.Json;
using System.Text.Json.Serialization;
using MemoryKeeper.Application;
using MemoryKeeper.Application.DTOs;
using MemoryKeeper.Application.Interfaces;
using Microsoft.Extensions.Logging;

namespace MemoryKeeper.Infrastructure.Location;

public sealed class GoogleLocationResolver : ILocationResolver
{
    private readonly HttpClient _httpClient;
    private readonly ISettingRepository _settingRepository;
    private readonly ILogger<GoogleLocationResolver> _logger;

    public GoogleLocationResolver(
        HttpClient httpClient,
        ISettingRepository settingRepository,
        ILogger<GoogleLocationResolver> logger)
    {
        _httpClient = httpClient;
        _settingRepository = settingRepository;
        _logger = logger;
    }

    public async Task<LocationResult?> ResolveAsync(
        double latitude,
        double longitude,
        CancellationToken cancellationToken = default)
    {
        var apiKey = await GetApiKeyAsync(cancellationToken);
        if (apiKey is null)
        {
            _logger.LogWarning("Google Maps API key is not configured. SettingKey={SettingKey}", SettingKeys.GoogleMapsApiKey);
            return null;
        }

        var inv = System.Globalization.CultureInfo.InvariantCulture;
        var latText = latitude.ToString(inv);
        var lngText = longitude.ToString(inv);

        var geocodeUri =
            $"https://maps.googleapis.com/maps/api/geocode/json?latlng={latText},{lngText}&language=ko&key={Uri.EscapeDataString(apiKey)}";

        using var geocodeResponse = await _httpClient.GetAsync(geocodeUri, cancellationToken);
        if (!geocodeResponse.IsSuccessStatusCode)
        {
            _logger.LogWarning(
                "Google Geocoding HTTP failed. StatusCode={StatusCode}",
                geocodeResponse.StatusCode);
            return null;
        }

        var geocodePayload = await geocodeResponse.Content.ReadFromJsonAsync<GoogleGeocodeResponse>(cancellationToken: cancellationToken);
        if (geocodePayload is null || !string.Equals(geocodePayload.Status, "OK", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning(
                "Google Geocoding returned non-OK status. Status={Status}, ErrorMessage={ErrorMessage}",
                geocodePayload?.Status,
                geocodePayload?.ErrorMessage);
            return null;
        }

        if (geocodePayload.Results.Count == 0)
        {
            return null;
        }

        var addressParts = ExtractAddressParts(geocodePayload.Results);
        var nearbyPoi = await TryResolveNearbyPoiAsync(apiKey, latitude, longitude, cancellationToken);

        if (nearbyPoi is not null)
        {
            return MergeAddress(nearbyPoi, addressParts, latitude, longitude);
        }

        var ranked = geocodePayload.Results
            .Select(result => new
            {
                Result = result,
                Rank = PlaceTypeCatalog.GetPriorityRank(result.Types)
            })
            .OrderBy(item => item.Rank)
            .ThenBy(item => item.Result.Types.Count)
            .First()
            .Result;

        if (!string.IsNullOrWhiteSpace(ranked.PlaceId) && PlaceTypeCatalog.IsVisitPoi(ranked.Types))
        {
            try
            {
                var details = await ResolvePlaceIdAsync(ranked.PlaceId, cancellationToken);
                if (details is not null)
                {
                    return MergeAddress(details, addressParts, latitude, longitude);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Place Details fallback failed. PlaceId={PlaceId}", ranked.PlaceId);
            }
        }

        var primaryType = PlaceTypeCatalog.SelectPrimaryType(ranked.Types);
        var displayName = !string.IsNullOrWhiteSpace(addressParts.City)
            ? addressParts.City
            : !string.IsNullOrWhiteSpace(addressParts.Province)
                ? addressParts.Province
                : ranked.FormattedAddress ?? string.Empty;

        return new LocationResult
        {
            DisplayName = displayName,
            Country = addressParts.Country,
            Province = addressParts.Region,
            City = addressParts.City,
            Address = ranked.FormattedAddress ?? displayName,
            PostalCode = addressParts.PostalCode,
            Latitude = latitude,
            Longitude = longitude,
            PlaceId = ranked.PlaceId,
            PlaceType = primaryType
        };
    }

    public async Task<IReadOnlyList<NearbyPlaceCandidateDto>> SearchNearbyAsync(
        double latitude,
        double longitude,
        int maxResults = 5,
        CancellationToken cancellationToken = default)
    {
        var apiKey = await GetApiKeyAsync(cancellationToken);
        if (apiKey is null)
        {
            throw new InvalidOperationException(
                "Google API Key가 없거나 형식이 올바르지 않습니다. 설정 → Google API에서 AIza… Key를 저장하세요.");
        }

        var payload = await FetchNearbySearchAsync(apiKey, latitude, longitude, cancellationToken);
        if (payload is null)
        {
            return [];
        }

        var take = Math.Clamp(maxResults, 1, 20);
        return payload.Results
            .Where(item => !string.IsNullOrWhiteSpace(item.PlaceId) && !string.IsNullOrWhiteSpace(item.Name))
            .Where(item => PlaceTypeCatalog.IsVisitPoi(item.Types))
            .Select(item =>
            {
                var loc = item.Geometry?.Location;
                var lat = loc?.Lat ?? latitude;
                var lng = loc?.Lng ?? longitude;
                var distance = loc is null
                    ? double.MaxValue
                    : GeoMath.DistanceMeters(latitude, longitude, loc.Lat, loc.Lng);

                return new NearbyPlaceCandidateDto
                {
                    GooglePlaceId = item.PlaceId,
                    Name = item.Name!,
                    Vicinity = item.Vicinity ?? string.Empty,
                    PlaceType = PlaceTypeCatalog.SelectPrimaryType(item.Types),
                    Latitude = lat,
                    Longitude = lng,
                    DistanceMeters = distance
                };
            })
            .OrderBy(item => item.DistanceMeters)
            .Take(take)
            .ToList();
    }

    private async Task<LocationResult?> TryResolveNearbyPoiAsync(
        string apiKey,
        double latitude,
        double longitude,
        CancellationToken cancellationToken)
    {
        var payload = await FetchNearbySearchAsync(apiKey, latitude, longitude, cancellationToken);
        if (payload is null)
        {
            return null;
        }

        var candidates = payload.Results
            .Where(item => !string.IsNullOrWhiteSpace(item.PlaceId) && !string.IsNullOrWhiteSpace(item.Name))
            .Where(item => PlaceTypeCatalog.IsVisitPoi(item.Types))
            .Select(item => new
            {
                Item = item,
                Rank = PlaceTypeCatalog.GetPriorityRank(item.Types),
                Distance = item.Geometry?.Location is { } loc
                    ? GeoMath.DistanceMeters(latitude, longitude, loc.Lat, loc.Lng)
                    : double.MaxValue,
                ScriptRank = GetNameScriptRank(item.Name!)
            })
            .Where(item => item.Distance <= 250)
            .OrderBy(item => item.ScriptRank)
            .ThenBy(item => item.Rank)
            .ThenBy(item => item.Distance)
            .Take(8)
            .ToList();

        var best = candidates.FirstOrDefault();
        if (best is null)
        {
            return null;
        }

        // Prefer a major landmark (amusement park etc.) when the closest POI name is Japanese kana-only.
        if (best.ScriptRank >= 2)
        {
            var landmark = candidates.FirstOrDefault(item =>
                item.Item.Types.Any(type =>
                    type is "amusement_park" or "museum" or "zoo" or "aquarium" or "shopping_mall")
                && item.ScriptRank < 2);
            if (landmark is not null)
            {
                best = landmark;
            }
        }

        try
        {
            var details = await ResolvePlaceIdAsync(best.Item.PlaceId, cancellationToken);
            if (details is not null)
            {
                var displayName = string.IsNullOrWhiteSpace(details.DisplayName)
                    ? best.Item.Name!
                    : details.DisplayName;

                if (GetNameScriptRank(displayName) >= 2 && GetNameScriptRank(best.Item.Name!) < GetNameScriptRank(displayName))
                {
                    displayName = best.Item.Name!;
                }

                return details with
                {
                    DisplayName = displayName,
                    PlaceType = details.PlaceType
                        ?? PlaceTypeCatalog.SelectPrimaryType(best.Item.Types)
                };
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Nearby POI Place Details failed. PlaceId={PlaceId}", best.Item.PlaceId);
        }

        return new LocationResult
        {
            DisplayName = best.Item.Name!,
            Latitude = latitude,
            Longitude = longitude,
            PlaceId = best.Item.PlaceId,
            PlaceType = PlaceTypeCatalog.SelectPrimaryType(best.Item.Types),
            Address = best.Item.Vicinity ?? best.Item.Name!
        };
    }

    private static int GetNameScriptRank(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return 3;
        }

        if (name.Any(ch => ch is >= '\uAC00' and <= '\uD7A3'))
        {
            return 0;
        }

        if (name.Any(ch => ch is (>= '\u3040' and <= '\u309F') or (>= '\u30A0' and <= '\u30FF')))
        {
            return 2;
        }

        return 1;
    }

    private async Task<GoogleNearbySearchResponse?> FetchNearbySearchAsync(
        string apiKey,
        double latitude,
        double longitude,
        CancellationToken cancellationToken)
    {
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        var requestUri =
            $"https://maps.googleapis.com/maps/api/place/nearbysearch/json?location={latitude.ToString(inv)},{longitude.ToString(inv)}&rankby=distance&language=ko&key={Uri.EscapeDataString(apiKey)}";

        using var response = await _httpClient.GetAsync(requestUri, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("Google Places Nearby Search HTTP failed. StatusCode={StatusCode}", response.StatusCode);
            return null;
        }

        var payload = await response.Content.ReadFromJsonAsync<GoogleNearbySearchResponse>(cancellationToken: cancellationToken);
        if (payload is null
            || (!string.Equals(payload.Status, "OK", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(payload.Status, "ZERO_RESULTS", StringComparison.OrdinalIgnoreCase)))
        {
            _logger.LogWarning(
                "Google Places Nearby Search non-OK. Status={Status}, ErrorMessage={ErrorMessage}",
                payload?.Status,
                payload?.ErrorMessage);
            return null;
        }

        return payload;
    }

    private static AddressParts ExtractAddressParts(IReadOnlyList<GoogleGeocodeResult> results)
    {
        string? country = null;
        string? province = null;
        string? city = null;
        string? region = null;
        string? postal = null;

        foreach (var result in results)
        {
            country ??= GetComponent(result, "country");
            province ??= GetComponent(result, "administrative_area_level_1");
            city ??= GetComponent(result, "locality");
            region ??= GetComponent(result, "sublocality_level_1")
                ?? GetComponent(result, "sublocality")
                ?? GetComponent(result, "ward")
                ?? GetComponent(result, "administrative_area_level_2");
            postal ??= GetComponent(result, "postal_code");
        }

        city ??= region ?? province;
        region ??= province;

        return new AddressParts(
            Country: country ?? string.Empty,
            Province: province ?? string.Empty,
            City: city ?? string.Empty,
            Region: region ?? string.Empty,
            PostalCode: postal ?? string.Empty);
    }

    private static LocationResult MergeAddress(
        LocationResult poi,
        AddressParts address,
        double latitude,
        double longitude)
    {
        return new LocationResult
        {
            DisplayName = poi.DisplayName,
            Country = string.IsNullOrWhiteSpace(poi.Country) ? address.Country : poi.Country,
            Province = !string.IsNullOrWhiteSpace(address.Region)
                ? address.Region
                : !string.IsNullOrWhiteSpace(poi.Province)
                    ? poi.Province
                    : address.Province,
            City = string.IsNullOrWhiteSpace(poi.City) ? address.City : poi.City,
            Address = string.IsNullOrWhiteSpace(poi.Address) ? address.City : poi.Address,
            PostalCode = string.IsNullOrWhiteSpace(poi.PostalCode) ? address.PostalCode : poi.PostalCode,
            Latitude = latitude,
            Longitude = longitude,
            PlaceId = poi.PlaceId,
            PlaceType = poi.PlaceType
        };
    }

    private readonly record struct AddressParts(
        string Country,
        string Province,
        string City,
        string Region,
        string PostalCode);

    public async Task<LocationResult?> ResolveAddressAsync(
        string address,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(address);

        var apiKey = await GetApiKeyAsync(cancellationToken);
        if (apiKey is null)
        {
            _logger.LogWarning("Google Maps API key is not configured. SettingKey={SettingKey}", SettingKeys.GoogleMapsApiKey);
            return null;
        }

        var requestUri =
            $"https://maps.googleapis.com/maps/api/geocode/json?address={Uri.EscapeDataString(address.Trim())}&language=ko&key={Uri.EscapeDataString(apiKey)}";

        using var response = await _httpClient.GetAsync(requestUri, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("Google forward Geocoding HTTP failed. StatusCode={StatusCode}", response.StatusCode);
            return null;
        }

        var payload = await response.Content.ReadFromJsonAsync<GoogleGeocodeResponse>(cancellationToken: cancellationToken);
        if (payload is null || !string.Equals(payload.Status, "OK", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning(
                "Google forward Geocoding returned non-OK status. Status={Status}, ErrorMessage={ErrorMessage}",
                payload?.Status,
                payload?.ErrorMessage);
            return null;
        }

        var first = payload.Results.FirstOrDefault();
        if (first?.Geometry?.Location is null)
        {
            return null;
        }

        var latitude = first.Geometry.Location.Lat;
        var longitude = first.Geometry.Location.Lng;
        var country = GetComponent(first, "country");
        var province = GetComponent(first, "administrative_area_level_1");
        var city = GetComponent(first, "locality")
            ?? GetComponent(first, "sublocality")
            ?? GetComponent(first, "administrative_area_level_2");
        var formatted = first.FormattedAddress ?? address.Trim();
        var displayName = !string.IsNullOrWhiteSpace(city)
            ? city
            : !string.IsNullOrWhiteSpace(province)
                ? province
                : formatted;

        return new LocationResult
        {
            DisplayName = displayName,
            Country = country ?? string.Empty,
            Province = province ?? string.Empty,
            City = city ?? string.Empty,
            Address = formatted,
            Latitude = latitude,
            Longitude = longitude
        };
    }

    public async Task<IReadOnlyList<PlaceSuggestionDto>> SuggestPlacesAsync(
        string input,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(input) || input.Trim().Length < 2)
        {
            return [];
        }

        var apiKey = await GetApiKeyAsync(cancellationToken);
        if (apiKey is null)
        {
            throw new InvalidOperationException(
                "Google API Key가 없거나 형식이 올바르지 않습니다. 설정 → Google API에서 AIza… Key를 저장하세요.");
        }

        var requestUri =
            $"https://maps.googleapis.com/maps/api/place/autocomplete/json?input={Uri.EscapeDataString(input.Trim())}&language=ko&key={Uri.EscapeDataString(apiKey)}";

        using var response = await _httpClient.GetAsync(requestUri, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("Google Places Autocomplete HTTP failed. StatusCode={StatusCode}", response.StatusCode);
            throw new InvalidOperationException($"Google Places 요청 실패 (HTTP {(int)response.StatusCode}).");
        }

        var payload = await response.Content.ReadFromJsonAsync<GooglePlacesAutocompleteResponse>(cancellationToken: cancellationToken);
        if (payload is null || !string.Equals(payload.Status, "OK", StringComparison.OrdinalIgnoreCase))
        {
            if (string.Equals(payload?.Status, "ZERO_RESULTS", StringComparison.OrdinalIgnoreCase))
            {
                return [];
            }

            _logger.LogWarning(
                "Google Places Autocomplete non-OK. Status={Status}, ErrorMessage={ErrorMessage}",
                payload?.Status,
                payload?.ErrorMessage);

            var detail = string.IsNullOrWhiteSpace(payload?.ErrorMessage)
                ? payload?.Status ?? "UNKNOWN"
                : $"{payload.Status}: {payload.ErrorMessage}";
            throw new InvalidOperationException(
                $"주소 자동완성에 실패했습니다. ({detail}) Maps·Geocoding·Places(레거시) API 활성화와 Key 제한을 확인하세요.");
        }

        return payload.Predictions
            .Where(item => !string.IsNullOrWhiteSpace(item.PlaceId))
            .Select(item => new PlaceSuggestionDto
            {
                PlaceId = item.PlaceId,
                PrimaryText = item.StructuredFormatting?.MainText
                    ?? item.Description
                    ?? string.Empty,
                SecondaryText = item.StructuredFormatting?.SecondaryText ?? string.Empty,
                Description = item.Description ?? string.Empty
            })
            .ToList();
    }

    public async Task<LocationResult?> ResolvePlaceIdAsync(
        string placeId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(placeId);

        var apiKey = await GetApiKeyAsync(cancellationToken);
        if (apiKey is null)
        {
            throw new InvalidOperationException(
                "Google API Key가 없거나 형식이 올바르지 않습니다. 설정 → Google API에서 AIza… Key를 저장하세요.");
        }

        var requestUri =
            $"https://maps.googleapis.com/maps/api/place/details/json?place_id={Uri.EscapeDataString(placeId.Trim())}&fields=place_id,formatted_address,geometry,address_components,name,types&language=ko&key={Uri.EscapeDataString(apiKey)}";

        using var response = await _httpClient.GetAsync(requestUri, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("Google Place Details HTTP failed. StatusCode={StatusCode}", response.StatusCode);
            throw new InvalidOperationException($"Google Place Details 요청 실패 (HTTP {(int)response.StatusCode}).");
        }

        var payload = await response.Content.ReadFromJsonAsync<GooglePlaceDetailsResponse>(cancellationToken: cancellationToken);
        if (payload is null || !string.Equals(payload.Status, "OK", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning(
                "Google Place Details non-OK. Status={Status}, ErrorMessage={ErrorMessage}",
                payload?.Status,
                payload?.ErrorMessage);

            var detail = string.IsNullOrWhiteSpace(payload?.ErrorMessage)
                ? payload?.Status ?? "UNKNOWN"
                : $"{payload.Status}: {payload.ErrorMessage}";
            throw new InvalidOperationException($"장소 상세 조회에 실패했습니다. ({detail})");
        }

        var result = payload.Result;
        if (result?.Geometry?.Location is null)
        {
            return null;
        }

        var latitude = result.Geometry.Location.Lat;
        var longitude = result.Geometry.Location.Lng;
        var country = GetPlaceComponent(result, "country");
        var province = GetPlaceComponent(result, "administrative_area_level_1");
        var region = GetPlaceComponent(result, "sublocality_level_1")
            ?? GetPlaceComponent(result, "sublocality")
            ?? GetPlaceComponent(result, "ward")
            ?? GetPlaceComponent(result, "administrative_area_level_2");
        var city = GetPlaceComponent(result, "locality")
            ?? region
            ?? GetPlaceComponent(result, "administrative_area_level_2");
        var address = result.FormattedAddress ?? result.Name ?? string.Empty;
        var postalCode = GetPlaceComponent(result, "postal_code");
        var displayName = !string.IsNullOrWhiteSpace(result.Name)
            ? result.Name
            : !string.IsNullOrWhiteSpace(city)
                ? city
                : address;
        var placeType = PlaceTypeCatalog.SelectPrimaryType(result.Types);

        return new LocationResult
        {
            DisplayName = displayName,
            Country = country ?? string.Empty,
            Province = region ?? province ?? string.Empty,
            City = city ?? string.Empty,
            Address = address,
            PostalCode = postalCode ?? string.Empty,
            Latitude = latitude,
            Longitude = longitude,
            PlaceId = result.PlaceId ?? placeId.Trim(),
            PlaceType = placeType
        };
    }

    private async Task<string?> GetApiKeyAsync(CancellationToken cancellationToken)
    {
        var apiKeySetting = await _settingRepository.GetByKeyAsync(SettingKeys.GoogleMapsApiKey, cancellationToken);
        var apiKey = GoogleMapsApiKeyValidator.NormalizeOrNull(apiKeySetting?.Value);
        if (apiKey is null)
        {
            _logger.LogWarning(
                "Google Maps API key is missing or invalid. SettingKey={SettingKey}",
                SettingKeys.GoogleMapsApiKey);
        }

        return apiKey;
    }

    private static string? GetComponent(GoogleGeocodeResult result, string type)
    {
        return result.AddressComponents
            .FirstOrDefault(component => component.Types.Contains(type, StringComparer.OrdinalIgnoreCase))
            ?.LongName;
    }

    private static string? GetPlaceComponent(GooglePlaceDetailsResult result, string type)
    {
        return result.AddressComponents
            .FirstOrDefault(component => component.Types.Contains(type, StringComparer.OrdinalIgnoreCase))
            ?.LongName;
    }

    private sealed class GoogleGeocodeResponse
    {
        [JsonPropertyName("status")]
        public string Status { get; set; } = string.Empty;

        [JsonPropertyName("error_message")]
        public string? ErrorMessage { get; set; }

        [JsonPropertyName("results")]
        public List<GoogleGeocodeResult> Results { get; set; } = [];
    }

    private sealed class GoogleGeocodeResult
    {
        [JsonPropertyName("place_id")]
        public string? PlaceId { get; set; }

        [JsonPropertyName("formatted_address")]
        public string? FormattedAddress { get; set; }

        [JsonPropertyName("types")]
        public List<string> Types { get; set; } = [];

        [JsonPropertyName("address_components")]
        public List<GoogleAddressComponent> AddressComponents { get; set; } = [];

        [JsonPropertyName("geometry")]
        public GoogleGeometry? Geometry { get; set; }
    }

    private sealed class GoogleGeometry
    {
        [JsonPropertyName("location")]
        public GoogleLatLng? Location { get; set; }
    }

    private sealed class GoogleLatLng
    {
        [JsonPropertyName("lat")]
        public double Lat { get; set; }

        [JsonPropertyName("lng")]
        public double Lng { get; set; }
    }

    private sealed class GoogleAddressComponent
    {
        [JsonPropertyName("long_name")]
        public string LongName { get; set; } = string.Empty;

        [JsonPropertyName("types")]
        public List<string> Types { get; set; } = [];
    }

    private sealed class GooglePlacesAutocompleteResponse
    {
        [JsonPropertyName("status")]
        public string Status { get; set; } = string.Empty;

        [JsonPropertyName("error_message")]
        public string? ErrorMessage { get; set; }

        [JsonPropertyName("predictions")]
        public List<GooglePlacePrediction> Predictions { get; set; } = [];
    }

    private sealed class GooglePlacePrediction
    {
        [JsonPropertyName("description")]
        public string? Description { get; set; }

        [JsonPropertyName("place_id")]
        public string PlaceId { get; set; } = string.Empty;

        [JsonPropertyName("structured_formatting")]
        public GoogleStructuredFormatting? StructuredFormatting { get; set; }
    }

    private sealed class GoogleStructuredFormatting
    {
        [JsonPropertyName("main_text")]
        public string? MainText { get; set; }

        [JsonPropertyName("secondary_text")]
        public string? SecondaryText { get; set; }
    }

    private sealed class GooglePlaceDetailsResponse
    {
        [JsonPropertyName("status")]
        public string Status { get; set; } = string.Empty;

        [JsonPropertyName("error_message")]
        public string? ErrorMessage { get; set; }

        [JsonPropertyName("result")]
        public GooglePlaceDetailsResult? Result { get; set; }
    }

    private sealed class GooglePlaceDetailsResult
    {
        [JsonPropertyName("place_id")]
        public string? PlaceId { get; set; }

        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("formatted_address")]
        public string? FormattedAddress { get; set; }

        [JsonPropertyName("types")]
        public List<string> Types { get; set; } = [];

        [JsonPropertyName("address_components")]
        public List<GoogleAddressComponent> AddressComponents { get; set; } = [];

        [JsonPropertyName("geometry")]
        public GoogleGeometry? Geometry { get; set; }
    }

    private sealed class GoogleNearbySearchResponse
    {
        [JsonPropertyName("status")]
        public string Status { get; set; } = string.Empty;

        [JsonPropertyName("error_message")]
        public string? ErrorMessage { get; set; }

        [JsonPropertyName("results")]
        public List<GoogleNearbyResult> Results { get; set; } = [];
    }

    private sealed class GoogleNearbyResult
    {
        [JsonPropertyName("place_id")]
        public string PlaceId { get; set; } = string.Empty;

        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("vicinity")]
        public string? Vicinity { get; set; }

        [JsonPropertyName("types")]
        public List<string> Types { get; set; } = [];

        [JsonPropertyName("geometry")]
        public GoogleGeometry? Geometry { get; set; }
    }
}
