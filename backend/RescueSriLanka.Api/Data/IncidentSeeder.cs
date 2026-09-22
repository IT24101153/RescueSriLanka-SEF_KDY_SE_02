using Microsoft.EntityFrameworkCore;
using RescueSriLanka.Api.Models;

namespace RescueSriLanka.Api.Data;

/// <summary>
/// Sample incidents so the map and dashboard have something to render during
/// development. Controlled by "SeedSampleIncidents" in appsettings — set it to
/// false and this never runs. Only inserts when the table is empty, so it will
/// not fight with incidents you create yourself.
/// </summary>
public static class IncidentSeeder
{
    private record Sample(
        string Title, string Description, IncidentType Type, IncidentSeverity Severity,
        IncidentStatus Status, double Lat, double Lng, int Radius, string District,
        int? People, int HoursAgo, int? AiScore, string? AiRationale);

    private static readonly Sample[] Samples =
    [
        new("Kelani River flooding — Kolonnawa",
            "River level above the flood threshold. Low-lying homes along the bank are inundated; several families moved to higher ground.",
            IncidentType.Flood, IncidentSeverity.Critical, IncidentStatus.InProgress,
            6.9350, 79.8890, 3200, "Colombo", 2400, 3, 88,
            "Sustained rainfall over 48h combined with high river gauge readings and dense residential population within the affected radius."),

        new("Landslide warning — Hanthana slope",
            "Soil movement observed on the slope above the access road after continuous rain. Cracks visible in the embankment.",
            IncidentType.Landslide, IncidentSeverity.High, IncidentStatus.Verified,
            7.2760, 80.6350, 1800, "Kandy", 340, 7, 74,
            "Saturated soil on a steep gradient with prior landslide history in the division."),

        new("Flash flooding — Ratnapura town",
            "Kalu Ganga overflow affecting the town centre. Shops and the main bus stand under water.",
            IncidentType.Flood, IncidentSeverity.High, IncidentStatus.InProgress,
            6.6828, 80.3992, 2500, "Ratnapura", 1100, 11, 71,
            "Rapid river rise with commercial density in the flood footprint."),

        new("Structure fire — Pettah market block",
            "Fire in a textile warehouse spreading to adjoining units. Heavy smoke across the market area.",
            IncidentType.Fire, IncidentSeverity.High, IncidentStatus.InProgress,
            6.9370, 79.8560, 600, "Colombo", 180, 2, 69,
            "Confined urban fire with high occupancy and limited vehicle access."),

        new("Coastal storm surge — Galle Fort seafront",
            "Strong winds and surge overtopping the sea wall. Access road to the fort partially closed.",
            IncidentType.Storm, IncidentSeverity.Moderate, IncidentStatus.Verified,
            6.0270, 80.2170, 1500, "Galle", 260, 16, 52,
            "Moderate surge with tourist presence along the affected seafront."),

        new("Earth slip blocking A5 — Nuwara Eliya",
            "Debris across one lane of the Nuwara Eliya–Badulla road. Traffic reduced to single file.",
            IncidentType.Landslide, IncidentSeverity.Moderate, IncidentStatus.Verified,
            6.9497, 80.7891, 900, "Nuwara Eliya", 40, 20, 46,
            "Road obstruction with no reported structural damage or casualties."),

        new("Multi-vehicle collision — Southern Expressway",
            "Collision near the Kokmaduwa interchange. One lane closed while recovery is under way.",
            IncidentType.Accident, IncidentSeverity.Moderate, IncidentStatus.Reported,
            5.9950, 80.4300, 400, "Matara", 12, 1, null, null),

        new("Drainage overflow — Kegalle bus stand",
            "Blocked culvert causing standing water around the bus stand. Passable but disruptive.",
            IncidentType.Flood, IncidentSeverity.Low, IncidentStatus.Reported,
            7.2513, 80.3464, 500, "Kegalle", 30, 5, null, null),

        new("Lightning damage — Anuradhapura tank bund",
            "Transformer damaged by a lightning strike. Power outage affecting nearby homes.",
            IncidentType.Storm, IncidentSeverity.Low, IncidentStatus.Resolved,
            8.3114, 80.4037, 700, "Anuradhapura", 90, 40, 22,
            "Localised utility damage with no injuries reported."),

        new("Lagoon flooding — Batticaloa",
            "Lagoon overflow affecting the low-lying approach road after monsoon rain.",
            IncidentType.Flood, IncidentSeverity.Moderate, IncidentStatus.Verified,
            7.7102, 81.6924, 2000, "Batticaloa", 480, 26, 55,
            "Recurrent seasonal flooding in a known low-elevation area."),
    ];

    public static async Task SeedAsync(AppDbContext db, ILogger logger, CancellationToken ct = default)
    {
        if (await db.Incidents.AnyAsync(ct))
        {
            logger.LogInformation("Incidents already present — sample data not seeded.");
            return;
        }

        var now = DateTime.UtcNow;

        foreach (var sample in Samples)
        {
            var reportedAt = now.AddHours(-sample.HoursAgo);
            var closed = sample.Status is IncidentStatus.Resolved or IncidentStatus.Rejected;

            db.Incidents.Add(new Incident
            {
                Title = sample.Title,
                Description = sample.Description,
                Type = sample.Type,
                Severity = sample.Severity,
                Status = sample.Status,
                Latitude = sample.Lat,
                Longitude = sample.Lng,
                AffectedRadiusMeters = sample.Radius,
                District = sample.District,
                EstimatedAffectedPeople = sample.People,
                ReportedAt = reportedAt,
                CreatedAt = reportedAt,
                IsActive = !closed,
                ResolvedAt = closed ? reportedAt.AddHours(6) : null,
                VerifiedAt = sample.Status == IncidentStatus.Reported ? null : reportedAt.AddMinutes(25),
                AiSeverity = sample.AiScore is null ? null : sample.Severity,
                AiSeverityScore = sample.AiScore,
                AiConfidence = sample.AiScore is null ? null : Math.Round(0.6 + sample.AiScore.Value / 400.0, 2),
                AiRationale = sample.AiRationale,
                AiAnalysedAt = sample.AiScore is null ? null : reportedAt.AddMinutes(2)
            });
        }

        await db.SaveChangesAsync(ct);
        logger.LogInformation("Seeded {Count} sample incidents.", Samples.Length);
    }
}
