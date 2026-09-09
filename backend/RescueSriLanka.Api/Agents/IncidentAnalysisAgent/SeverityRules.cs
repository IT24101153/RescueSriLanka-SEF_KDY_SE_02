using RescueSriLanka.Api.Models;

namespace RescueSriLanka.Api.Agents.IncidentAnalysisAgent;

/// <summary>
/// Deterministic severity scoring.
///
/// This is the agent's floor, not a placeholder. It runs when the language
/// model is unconfigured, unreachable, rate limited, or returns something that
/// fails validation — so an incident is never left unscored, and a failure is
/// recorded rather than silently swallowed. It is also fully explainable, which
/// matters when a coordinator asks why a report was graded Critical.
/// </summary>
public static class SeverityRules
{
    /// <summary>Base hazard weight by disaster type, 0-40.</summary>
    private static int TypeWeight(IncidentType type) => type switch
    {
        IncidentType.Tsunami => 40,
        IncidentType.Landslide => 32,
        IncidentType.Flood => 28,
        IncidentType.Fire => 26,
        IncidentType.Storm => 20,
        IncidentType.Accident => 14,
        _ => 12
    };

    /// <summary>People at risk, 0-30.</summary>
    private static int PeopleWeight(int? people) => people switch
    {
        null => 8,
        >= 2000 => 30,
        >= 500 => 24,
        >= 100 => 17,
        >= 20 => 11,
        _ => 5
    };

    /// <summary>Clustered reports mean a bigger event than any one report shows, 0-20.</summary>
    private static int ClusterWeight(int nearbyIncidents) => nearbyIncidents switch
    {
        >= 5 => 20,
        >= 3 => 15,
        >= 1 => 8,
        _ => 0
    };

    /// <summary>Rain is the driver behind Sri Lankan floods and landslides, 0-10.</summary>
    private static int RainWeight(double? rainfallMm48h, IncidentType type)
    {
        if (rainfallMm48h is null) return 0;
        if (type is not (IncidentType.Flood or IncidentType.Landslide)) return 0;

        return rainfallMm48h switch
        {
            >= 200 => 10,
            >= 100 => 7,
            >= 50 => 4,
            _ => 1
        };
    }

    public static IncidentAnalysisResult Score(
        IncidentAnalysisInput input,
        int nearbyIncidents,
        double? rainfallMm48h = null)
    {
        var score = Math.Clamp(
            TypeWeight(input.Type) +
            PeopleWeight(input.EstimatedAffectedPeople) +
            ClusterWeight(nearbyIncidents) +
            RainWeight(rainfallMm48h, input.Type),
            0, 100);

        var severity = score switch
        {
            >= 75 => IncidentSeverity.Critical,
            >= 55 => IncidentSeverity.High,
            >= 32 => IncidentSeverity.Moderate,
            _ => IncidentSeverity.Low
        };

        var zone = severity switch
        {
            IncidentSeverity.Critical or IncidentSeverity.High => ZoneStatus.Danger,
            _ => ZoneStatus.Caution
        };

        // Radius grows with severity and with the size of the exposed population.
        var radius = severity switch
        {
            IncidentSeverity.Critical => 3000,
            IncidentSeverity.High => 2000,
            IncidentSeverity.Moderate => 1200,
            _ => 600
        };
        if (input.EstimatedAffectedPeople >= 1000) radius += 800;

        var reasons = new List<string>
        {
            $"{input.Type} carries a base hazard weight of {TypeWeight(input.Type)}/40"
        };

        if (input.EstimatedAffectedPeople is int people)
            reasons.Add($"an estimated {people:N0} people are in the affected area");
        else
            reasons.Add("the number of people affected was not reported");

        if (nearbyIncidents > 0)
            reasons.Add($"{nearbyIncidents} other active incident(s) within 5 km suggest a wider event");

        if (rainfallMm48h is double rain)
            reasons.Add($"{rain:N0} mm of rain fell in the last 48 hours");

        return new IncidentAnalysisResult
        {
            Severity = severity,
            SeverityScore = score,
            // Rules are transparent but blunt — never claim model-level confidence.
            Confidence = 0.55,
            RecommendedZoneStatus = zone,
            RecommendedRadiusMeters = radius,
            Rationale =
                $"Rule-based assessment scored {score}/100: " +
                string.Join("; ", reasons) + ".",
            UsedFallback = true
        };
    }
}
