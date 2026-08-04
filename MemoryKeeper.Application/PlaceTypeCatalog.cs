namespace MemoryKeeper.Application;

/// <summary>
/// Google Places type ranking and UI hints for visit-POI place naming (MK-042M).
/// </summary>
public static class PlaceTypeCatalog
{
    /// <summary>
    /// Lower index = higher priority (more specific visit place).
    /// </summary>
    public static readonly string[] PriorityTypes =
    [
        "tourist_attraction",
        "amusement_park",
        "museum",
        "aquarium",
        "zoo",
        "park",
        "shopping_mall",
        "airport",
        "train_station",
        "establishment",
        "premise",
        "point_of_interest",
        "route",
        "neighborhood",
        "locality",
        "administrative_area_level_3",
        "administrative_area_level_2",
        "administrative_area_level_1",
        "administrative_area"
    ];

    private static readonly HashSet<string> AdministrativeTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "locality",
        "neighborhood",
        "route",
        "administrative_area",
        "administrative_area_level_1",
        "administrative_area_level_2",
        "administrative_area_level_3",
        "country",
        "postal_code",
        "political"
    };

    public static int GetPriorityRank(IEnumerable<string>? types)
    {
        if (types is null)
        {
            return int.MaxValue;
        }

        var best = int.MaxValue;
        foreach (var type in types)
        {
            var index = Array.FindIndex(
                PriorityTypes,
                candidate => string.Equals(candidate, type, StringComparison.OrdinalIgnoreCase));
            if (index >= 0 && index < best)
            {
                best = index;
            }
        }

        return best;
    }

    public static string? SelectPrimaryType(IEnumerable<string>? types)
    {
        if (types is null)
        {
            return null;
        }

        var list = types.ToList();
        if (list.Count == 0)
        {
            return null;
        }

        return list
            .OrderBy(type => GetPriorityRank([type]))
            .ThenBy(type => type, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
    }

    public static bool IsVisitPoi(IEnumerable<string>? types)
    {
        var rank = GetPriorityRank(types);
        // tourist_attraction .. point_of_interest are visit POIs; route+ are administrative-ish.
        var routeIndex = Array.FindIndex(
            PriorityTypes,
            type => string.Equals(type, "route", StringComparison.OrdinalIgnoreCase));
        return rank < routeIndex;
    }

    public static string GetIcon(string? placeType)
    {
        if (string.IsNullOrWhiteSpace(placeType))
        {
            return "📍";
        }

        return placeType.Trim().ToLowerInvariant() switch
        {
            "tourist_attraction" => "🎡",
            "amusement_park" => "🎡",
            "museum" => "🏰",
            "aquarium" => "🏰",
            "zoo" => "🏰",
            "church" or "hindu_temple" or "mosque" or "synagogue" or "place_of_worship" => "🏰",
            "shopping_mall" or "store" or "department_store" => "🛍",
            "park" => "🌲",
            "natural_feature" => "🏖",
            "lodging" => "🏨",
            "airport" => "✈",
            "train_station" or "subway_station" or "transit_station" or "bus_station" => "🚉",
            "restaurant" or "food" or "meal_takeaway" => "🍽",
            "cafe" or "bakery" => "☕",
            _ => "📍"
        };
    }

    public static double GetRecommendedRadiusMeters(string? placeType)
    {
        if (string.IsNullOrWhiteSpace(placeType))
        {
            return PlaceCategoryDefaults.GetRecommendedRadius(null);
        }

        return placeType.Trim().ToLowerInvariant() switch
        {
            "amusement_park" => 500,
            "airport" => 800,
            "park" or "zoo" => 300,
            "shopping_mall" => 200,
            "tourist_attraction" or "museum" or "aquarium" => 150,
            "train_station" => 120,
            "cafe" or "restaurant" => 50,
            _ when AdministrativeTypes.Contains(placeType) => 200,
            _ => 100
        };
    }

    public static string FormatTitleWithIcon(string title, string? placeType)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return $"{GetIcon(placeType)} {title}";
        }

        return $"{GetIcon(placeType)} {title.Trim()}";
    }
}
