using RescueSriLanka.Api.Services;

namespace RescueSriLanka.Api.Features.ComponentA.Agents.Shared;

/// <summary>
/// The main town of each of the 25 districts — the same table the Flutter app
/// bundles. Used to check that the district a reporter typed is plausible for
/// where they dropped the pin. A town is a rough anchor, not a boundary, so
/// the nearest town is a suggestion for a coordinator, never applied silently.
/// </summary>
public static class DistrictLocator
{
    public static readonly IReadOnlyDictionary<string, (double Lat, double Lng)> Towns =
        new Dictionary<string, (double, double)>
        {
            ["Ampara"] = (7.2912, 81.6724),
            ["Anuradhapura"] = (8.3114, 80.4037),
            ["Badulla"] = (6.9934, 81.0550),
            ["Batticaloa"] = (7.7310, 81.6747),
            ["Colombo"] = (6.9271, 79.8612),
            ["Galle"] = (6.0535, 80.2210),
            ["Gampaha"] = (7.0873, 80.0144),
            ["Hambantota"] = (6.1241, 81.1185),
            ["Jaffna"] = (9.6615, 80.0255),
            ["Kalutara"] = (6.5854, 79.9607),
            ["Kandy"] = (7.2906, 80.6337),
            ["Kegalle"] = (7.2513, 80.3464),
            ["Kilinochchi"] = (9.3803, 80.3770),
            ["Kurunegala"] = (7.4863, 80.3647),
            ["Mannar"] = (8.9810, 79.9044),
            ["Matale"] = (7.4675, 80.6234),
            ["Matara"] = (5.9549, 80.5550),
            ["Monaragala"] = (6.8728, 81.3507),
            ["Mullaitivu"] = (9.2671, 80.8142),
            ["Nuwara Eliya"] = (6.9497, 80.7891),
            ["Polonnaruwa"] = (7.9403, 81.0188),
            ["Puttalam"] = (8.0362, 79.8283),
            ["Ratnapura"] = (6.6828, 80.3992),
            ["Trincomalee"] = (8.5874, 81.2152),
            ["Vavuniya"] = (8.7514, 80.4971),
        };

    /// <summary>The district whose main town is nearest, and how far it is in km.</summary>
    public static (string District, double DistanceKm) Nearest(double latitude, double longitude) =>
        Towns
            .Select(town => (town.Key, GeoService.DistanceKm(latitude, longitude, town.Value.Lat, town.Value.Lng)))
            .MinBy(row => row.Item2);
}
