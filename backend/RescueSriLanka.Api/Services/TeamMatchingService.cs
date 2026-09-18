using Microsoft.EntityFrameworkCore;
using RescueSriLanka.Api.Data;
using RescueSriLanka.Api.DTOs;
using RescueSriLanka.Api.Models;

namespace RescueSriLanka.Api.Services
{
    public interface ITeamMatchingService
    {
        // Ranks candidate teams for a given required skill (and optionally
        // location/capacity). This is the "skill/availability-based team
        // matching" business-specific operation for Component D.
        Task<List<TeamMatchResultDto>> FindMatchesAsync(MatchRequestDto request);
    }

    public class TeamMatchingService : ITeamMatchingService
    {
        private readonly ApplicationDbContext _db;

        public TeamMatchingService(ApplicationDbContext db)
        {
            _db = db;
        }

        public async Task<List<TeamMatchResultDto>> FindMatchesAsync(MatchRequestDto request)
        {
            var teams = await _db.RescueTeams
                .Include(t => t.Members)
                .Include(t => t.Vehicles)
                .Where(t => t.Status != TeamStatus.OffDuty)
                .ToListAsync();

            var results = new List<TeamMatchResultDto>();

            foreach (var team in teams)
            {
                var matchingAvailable = team.Members
                    .Count(m => m.IsAvailable && m.Skill == request.RequiredSkill);

                // A team with zero available members holding the required
                // skill is not a candidate at all.
                if (matchingAvailable == 0) continue;

                // If a minimum vehicle capacity was requested, require at
                // least one available vehicle that meets it.
                if (request.MinCapacity is int minCap)
                {
                    var hasCapableVehicle = team.Vehicles.Any(v =>
                        v.Status == VehicleStatus.Available && v.Capacity >= minCap);
                    if (!hasCapableVehicle) continue;
                }

                double? distanceKm = null;
                if (request.Latitude is double lat && request.Longitude is double lng
                    && team.BaseLatitude is double tLat && team.BaseLongitude is double tLng)
                {
                    distanceKm = HaversineKm(lat, lng, tLat, tLng);
                }

                // Simple scoring: more available skilled members is better,
                // an idle (Available) team beats a currently-busy one,
                // and closer is better when distance is known.
                int score = matchingAvailable * 10;
                score += team.Status == TeamStatus.Available ? 5 : 0;
                if (distanceKm is double d)
                {
                    score += d switch
                    {
                        <= 5 => 10,
                        <= 15 => 5,
                        <= 30 => 2,
                        _ => 0
                    };
                }

                results.Add(new TeamMatchResultDto(
                    team.Id, team.Name, matchingAvailable, distanceKm, score));
            }

            return results.OrderByDescending(r => r.Score).ToList();
        }

        // Great-circle distance between two lat/lng points, in kilometers.
        private static double HaversineKm(double lat1, double lon1, double lat2, double lon2)
        {
            const double R = 6371.0;
            double dLat = ToRad(lat2 - lat1);
            double dLon = ToRad(lon2 - lon1);
            double a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2)
                       + Math.Cos(ToRad(lat1)) * Math.Cos(ToRad(lat2))
                       * Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
            double c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
            return R * c;
        }

        private static double ToRad(double deg) => deg * Math.PI / 180.0;
    }
}
