using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using RescueSriLanka.Api.Data;
using RescueSriLanka.Api.Features.ComponentB.Models;
using RescueSriLanka.Api.Features.ComponentD.Data;
using TeamStatus = RescueSriLanka.Api.Features.ComponentD.Models.TeamStatus;

namespace RescueSriLanka.Api.Features.ComponentB.Agents.PlannerAgent;

/// <summary>
/// The read-only tools the assessment model may call. Each one answers from
/// records and writes nothing. A call to any name not in <see cref="AllowList"/>
/// is refused before it runs.
/// </summary>
public static class AssessmentTools
{
    public const string CountNearbyIncidents = "count_nearby_active_incidents";
    public const string ListAvailableTeams = "list_available_rescue_teams";
    public const string CountOpenRequests = "count_open_help_requests_nearby";

    public const double DefaultRadiusKm = 5.0;
    public const double AssumedResponseSpeedKmh = 40.0;

    private const double MaxRadiusKm = 25.0;
    private const double MinRadiusKm = 0.5;
    private const int MaxTeamsListed = 5;

    // Degrees around the point. Comfortably wider than any radius allowed here at Sri Lankan latitudes;
    // the exact distance is checked afterwards.
    private const double SearchBox = 0.3;

    public static readonly IReadOnlyList<string> AllowList =
        [CountNearbyIncidents, ListAvailableTeams, CountOpenRequests];

    /// <summary>The function declarations offered to the model.</summary>
    public static readonly object[] Declarations =
    [
        new
        {
            name = CountNearbyIncidents,
            description = "Counts active incidents reported within a radius of this help request. Read-only.",
            parameters = RadiusParameter("Search radius in kilometres. Defaults to 5.")
        },
        new
        {
            name = ListAvailableTeams,
            description = "Lists up to five rescue teams that are available and have a recorded base, nearest first, with straight-line distance and an estimated ETA. Read-only.",
            parameters = NoParameters()
        },
        new
        {
            name = CountOpenRequests,
            description = "Counts other open help requests (pending, assigned or in progress) within a radius of this one. Read-only.",
            parameters = RadiusParameter("Search radius in kilometres. Defaults to 5.")
        }
    ];

    /// <summary>Runs one tool. The caller has already checked the name against <see cref="AllowList"/>.</summary>
    public static async Task<object> ExecuteAsync(
        string tool, JsonElement arguments, HelpRequest request,
        AppDbContext db, ComponentDDbContext componentD, CancellationToken ct)
    {
        if (tool == CountNearbyIncidents)
        {
            var radius = RadiusFrom(arguments);
            var count = await CountNearbyActiveIncidentsAsync(db, request.Latitude, request.Longitude, radius, ct);
            return new { radiusKm = radius, count };
        }

        if (tool == ListAvailableTeams)
        {
            return await SearchAvailableTeamsAsync(componentD, request.Latitude, request.Longitude, MaxTeamsListed, ct);
        }

        if (tool == CountOpenRequests)
        {
            var radius = RadiusFrom(arguments);
            var count = await CountOpenRequestsNearbyAsync(db, request, radius, ct);
            return new { radiusKm = radius, count };
        }

        throw new InvalidOperationException($"'{tool}' is not an assessment tool.");
    }

    /// <summary>Available teams with a recorded base, nearest first. Shared with the logistics step.</summary>
    public static async Task<TeamSearch> SearchAvailableTeamsAsync(
        ComponentDDbContext componentD, double latitude, double longitude, int max, CancellationToken ct = default)
    {
        var teams = await componentD.RescueTeams.AsNoTracking()
            .Where(team => team.Status == TeamStatus.Available
                && team.BaseLatitude != null
                && team.BaseLongitude != null)
            .Select(team => new { team.Name, Lat = team.BaseLatitude!.Value, Lng = team.BaseLongitude!.Value })
            .ToListAsync(ct);

        var nearest = teams
            .Select(team =>
            {
                var km = Geo.DistanceKm(latitude, longitude, team.Lat, team.Lng);
                return new TeamOffer(
                    team.Name,
                    Math.Round(km, 1),
                    Math.Round(km / AssumedResponseSpeedKmh * 60.0));
            })
            .OrderBy(offer => offer.DistanceKm)
            .Take(max)
            .ToList();

        return new TeamSearch(nearest, teams.Count);
    }

    /// <summary>Active incidents within a radius of a point.</summary>
    public static async Task<int> CountNearbyActiveIncidentsAsync(
        AppDbContext db, double latitude, double longitude, double radiusKm, CancellationToken ct = default)
    {
        var candidates = await db.Incidents.AsNoTracking()
            .Where(incident => incident.IsActive
                && incident.Latitude >= latitude - SearchBox && incident.Latitude <= latitude + SearchBox
                && incident.Longitude >= longitude - SearchBox && incident.Longitude <= longitude + SearchBox)
            .Select(incident => new { incident.Latitude, incident.Longitude })
            .ToListAsync(ct);

        return candidates.Count(incident =>
            Geo.DistanceKm(latitude, longitude, incident.Latitude, incident.Longitude) <= radiusKm);
    }

    /// <summary>Other open help requests within a radius of this one.</summary>
    public static async Task<int> CountOpenRequestsNearbyAsync(
        AppDbContext db, HelpRequest request, double radiusKm, CancellationToken ct = default)
    {
        var candidates = await db.HelpRequests.AsNoTracking()
            .Where(other => other.Id != request.Id
                && (other.Status == HelpRequestStatus.Pending
                    || other.Status == HelpRequestStatus.Assigned
                    || other.Status == HelpRequestStatus.InProgress)
                && other.Latitude >= request.Latitude - SearchBox && other.Latitude <= request.Latitude + SearchBox
                && other.Longitude >= request.Longitude - SearchBox && other.Longitude <= request.Longitude + SearchBox)
            .Select(other => new { other.Latitude, other.Longitude })
            .ToListAsync(ct);

        return candidates.Count(other =>
            Geo.DistanceKm(request.Latitude, request.Longitude, other.Latitude, other.Longitude) <= radiusKm);
    }

    private static double RadiusFrom(JsonElement arguments)
    {
        if (arguments.ValueKind == JsonValueKind.Object
            && arguments.TryGetProperty("radius_km", out var value)
            && value.ValueKind == JsonValueKind.Number
            && value.TryGetDouble(out var km))
        {
            return Math.Clamp(km, MinRadiusKm, MaxRadiusKm);
        }

        return DefaultRadiusKm;
    }

    private static object RadiusParameter(string description) => new
    {
        type = "OBJECT",
        properties = new { radius_km = new { type = "NUMBER", description } }
    };

    private static object NoParameters() => new { type = "OBJECT", properties = new { } };
}

public sealed record TeamOffer(string Name, double DistanceKm, double EstimatedEtaMinutes);

public sealed record TeamSearch(IReadOnlyList<TeamOffer> Nearest, int Considered);
