using RescueSriLanka.Api.Features.ComponentA.Models;
using RescueSriLanka.Api.Services;

namespace RescueSriLanka.Api.Features.ComponentA.Agents.ZonePlanningAgent;

/// <summary>
/// The deterministic parts of zone planning: the clustering tool the Evidence
/// agent uses, and the bounds the Validator holds a plan to. They never propose
/// a zone — every proposal comes from the language model.
/// </summary>
public static class ZonePlanningRules
{
    /// <summary>Incidents this close (km) are linked into one cluster.</summary>
    public const double LinkDistanceKm = 5;

    public const int MinRadiusMeters = 200;
    public const int MaxRadiusMeters = 20000;
    public const int MaxExpiryHours = 168;

    private static readonly IncidentType[] WeatherDriven =
        [IncidentType.Flood, IncidentType.Landslide, IncidentType.Storm, IncidentType.Tsunami];

    public static bool IsWeatherDriven(IncidentType type) => WeatherDriven.Contains(type);

    /// <summary>
    /// Single-linkage clustering: two incidents share a cluster when a chain of
    /// incidents each within <see cref="LinkDistanceKm"/> joins them. Only
    /// groups of two or more are returned — a lone incident has its own zone.
    /// </summary>
    public static IReadOnlyList<IncidentCluster> Cluster(IReadOnlyList<PlanningIncident> incidents)
    {
        var parent = Enumerable.Range(0, incidents.Count).ToArray();
        int Find(int i) => parent[i] == i ? i : parent[i] = Find(parent[i]);

        for (var i = 0; i < incidents.Count; i++)
        {
            for (var j = i + 1; j < incidents.Count; j++)
            {
                if (GeoService.DistanceKm(incidents[i].Latitude, incidents[i].Longitude,
                        incidents[j].Latitude, incidents[j].Longitude) <= LinkDistanceKm)
                {
                    parent[Find(i)] = Find(j);
                }
            }
        }

        return [.. incidents
            .Select((incident, index) => (incident, root: Find(index)))
            .GroupBy(row => row.root, row => row.incident)
            .Where(group => group.Count() >= 2)
            .OrderByDescending(group => group.Max(incident => incident.Severity))
            .ThenByDescending(group => group.Count())
            .Select((group, index) => Describe(index, [.. group]))];
    }

    private static IncidentCluster Describe(int index, List<PlanningIncident> members)
    {
        var lat = members.Average(m => m.Latitude);
        var lng = members.Average(m => m.Longitude);

        // Far enough from the centre to cover every member's own affected area.
        var span = members.Max(m =>
            GeoService.DistanceKm(lat, lng, m.Latitude, m.Longitude) * 1000 + m.RadiusMeters);

        return new IncidentCluster(
            index,
            [.. members.Select(m => m.Id)],
            Math.Round(lat, 5),
            Math.Round(lng, 5),
            (int)Math.Ceiling(span),
            members.Max(m => m.Severity),
            members.GroupBy(m => m.Type).OrderByDescending(g => g.Count()).First().Key,
            members.Where(m => m.District is not null).GroupBy(m => m.District!)
                .OrderByDescending(g => g.Count()).Select(g => g.Key).FirstOrDefault(),
            members.Sum(m => m.People ?? 0),
            null);
    }

    public static int RoundRadius(double meters) =>
        (int)Math.Clamp(Math.Ceiling(meters / 100) * 100, MinRadiusMeters, MaxRadiusMeters);
}
