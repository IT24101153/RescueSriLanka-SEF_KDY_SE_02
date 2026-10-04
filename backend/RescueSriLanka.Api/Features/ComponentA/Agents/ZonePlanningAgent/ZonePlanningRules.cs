using RescueSriLanka.Api.Features.ComponentA.Models;
using RescueSriLanka.Api.Services;

namespace RescueSriLanka.Api.Features.ComponentA.Agents.ZonePlanningAgent;

/// <summary>
/// Deterministic zone planning: the clustering tool the Evidence agent uses,
/// and the floor the Reasoning agent falls back to without a model.
///
/// Each incident already draws its own small derived zone. What a coordinator
/// lacks is the operational picture above that — "these four reports are one
/// flood; close the whole area" — and the housekeeping of retiring manual
/// zones whose hazard has gone. Those are the two things proposed here.
/// </summary>
public static class ZonePlanningRules
{
    /// <summary>Incidents this close (km) are linked into one cluster.</summary>
    public const double LinkDistanceKm = 5;

    public const int MinRadiusMeters = 200;
    public const int MaxRadiusMeters = 20000;
    public const int MaxExpiryHours = 168;

    /// <summary>Heavy rain keeps flood and landslide areas dangerous for longer.</summary>
    public const double HeavyRainMm = 100;

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

    /// <summary>The deterministic plan, used when the model's answer is unusable.</summary>
    public static ZonePlanResult Propose(ZonePlanningEvidence evidence)
    {
        var zones = new List<ZoneProposal>();

        foreach (var cluster in evidence.Clusters)
        {
            // An existing manual zone already covering this area is enough.
            if (evidence.ManualZones.Any(zone =>
                    zone.Status >= StatusFor(cluster.WorstSeverity) &&
                    GeoService.DistanceKm(zone.CenterLatitude, zone.CenterLongitude,
                        cluster.CenterLatitude, cluster.CenterLongitude) * 1000 <= zone.RadiusMeters))
            {
                continue;
            }

            var heavyRain = cluster.RainfallMm >= HeavyRainMm && IsWeatherDriven(cluster.DominantType);
            var status = StatusFor(cluster.WorstSeverity);
            var buffer = heavyRain ? 1500 : 500;

            zones.Add(new ZoneProposal
            {
                Action = ZoneActions.Create,
                Name = $"{cluster.DominantType} area — {cluster.District ?? "multi-incident"} ({cluster.IncidentIds.Count} incidents)",
                Status = status,
                CenterLatitude = cluster.CenterLatitude,
                CenterLongitude = cluster.CenterLongitude,
                RadiusMeters = RoundRadius(cluster.SpanRadiusMeters + buffer),
                District = cluster.District,
                ExpiresInHours = (status == ZoneStatus.Danger ? 48 : 24) + (heavyRain ? 24 : 0),
                Rationale = $"{cluster.IncidentIds.Count} approved {cluster.DominantType.ToString().ToLowerInvariant()} "
                            + $"incidents within {LinkDistanceKm:F0} km of each other, worst {cluster.WorstSeverity}"
                            + (cluster.People > 0 ? $", about {cluster.People:N0} people affected" : "")
                            + (heavyRain ? $", {cluster.RainfallMm:F0} mm rain in 48 h" : "") + ".",
                BasedOnIncidentIds = cluster.IncidentIds
            });
        }

        foreach (var zone in evidence.ManualZones)
        {
            var expired = zone.ExpiresAt is DateTime expiry && expiry <= DateTime.UtcNow;
            var quiet = zone.ActiveIncidentsInside == 0 && zone.ComputedAt <= DateTime.UtcNow.AddHours(-6);
            if (!expired && !quiet) continue;

            zones.Add(new ZoneProposal
            {
                Action = ZoneActions.Retire,
                ZoneId = zone.Id,
                Name = zone.Name,
                Status = zone.Status,
                CenterLatitude = zone.CenterLatitude,
                CenterLongitude = zone.CenterLongitude,
                RadiusMeters = zone.RadiusMeters,
                Rationale = expired
                    ? "Its expiry time has passed."
                    : "No approved active incident lies inside it any more."
            });
        }

        var creates = zones.Count(z => z.Action == ZoneActions.Create);
        var retires = zones.Count - creates;

        return new ZonePlanResult
        {
            Zones = zones,
            Summary = zones.Count == 0
                ? "Rule-based plan: the zone layer already matches the active incidents."
                : $"Rule-based plan: {creates} new area zone(s), {retires} zone(s) to retire.",
            UsedFallback = true
        };
    }

    public static ZoneStatus StatusFor(IncidentSeverity severity) =>
        severity >= IncidentSeverity.High ? ZoneStatus.Danger : ZoneStatus.Caution;

    public static int RoundRadius(double meters) =>
        (int)Math.Clamp(Math.Ceiling(meters / 100) * 100, MinRadiusMeters, MaxRadiusMeters);
}
