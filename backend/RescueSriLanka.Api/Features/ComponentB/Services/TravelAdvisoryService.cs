using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using RescueSriLanka.Api.Data;
using RescueSriLanka.Api.Features.ComponentB.DTOs;
using RescueSriLanka.Api.Features.ComponentB.Models;

namespace RescueSriLanka.Api.Features.ComponentB.Services
{
    public interface ITravelAdvisoryService
    {
        Task<TravelAdvisoryResponseDto> CreateAsync(CreateTravelAdvisoryDto dto);
        Task<TravelAdvisoryResponseDto?> GetByIdAsync(Guid id);
        Task<List<TravelAdvisoryResponseDto>> GetActiveAsync();
        Task<TravelAdvisoryResponseDto?> UpdateAsync(Guid id, UpdateTravelAdvisoryDto dto);
        Task<bool> DeleteAsync(Guid id);
        Task<SafetyCheckResponseDto> CheckSafetyAsync(SafetyCheckRequestDto dto);
    }

    public class TravelAdvisoryService(AppDbContext db) : ITravelAdvisoryService
    {
        private readonly AppDbContext _db = db;

        public async Task<TravelAdvisoryResponseDto> CreateAsync(CreateTravelAdvisoryDto dto)
        {
            var entity = new TravelAdvisory
            {
                AreaName = dto.AreaName,
                Latitude = dto.Latitude,
                Longitude = dto.Longitude,
                RadiusMeters = dto.RadiusMeters,
                SafetyLevel = dto.SafetyLevel,
                Reason = dto.Reason,
                ExpiresAt = dto.ExpiresAt
            };

            _db.TravelAdvisories.Add(entity);
            await _db.SaveChangesAsync();

            return ToDto(entity);
        }

        public async Task<List<TravelAdvisoryResponseDto>> GetActiveAsync()
        {
            var now = DateTime.UtcNow;
            var entities = await _db.TravelAdvisories
                .Where(a => a.ExpiresAt == null || a.ExpiresAt > now)
                .ToListAsync();

            return [.. entities.Select(ToDto)];
        }

        public async Task<TravelAdvisoryResponseDto?> GetByIdAsync(Guid id)
        {
            var entity = await _db.TravelAdvisories.FindAsync(id);
            return entity is null ? null : ToDto(entity);
        }

        public async Task<TravelAdvisoryResponseDto?> UpdateAsync(Guid id, UpdateTravelAdvisoryDto dto)
        {
            var entity = await _db.TravelAdvisories.FindAsync(id);
            if (entity is null) return null;

            entity.AreaName = dto.AreaName;
            entity.Latitude = dto.Latitude;
            entity.Longitude = dto.Longitude;
            entity.RadiusMeters = dto.RadiusMeters;
            entity.SafetyLevel = dto.SafetyLevel;
            entity.Reason = dto.Reason;
            entity.ExpiresAt = dto.ExpiresAt;
            await _db.SaveChangesAsync();

            return ToDto(entity);
        }

        public async Task<bool> DeleteAsync(Guid id)
        {
            var entity = await _db.TravelAdvisories.FindAsync(id);
            if (entity is null) return false;

            _db.TravelAdvisories.Remove(entity);
            await _db.SaveChangesAsync();
            return true;
        }

        // Business-specific operation: route/area safety check.
        // Checks each given point against every active advisory using distance.
        // Returns the WORST safety level found (Danger > Caution > Safe), with the reason.
        public async Task<SafetyCheckResponseDto> CheckSafetyAsync(SafetyCheckRequestDto dto)
        {
            var activeAdvisories = await GetActiveAdvisoryEntitiesAsync();
            var matched = new List<TravelAdvisory>();

            foreach (var point in dto.Points)
            {
                foreach (var advisory in activeAdvisories)
                {
                    double distanceMeters = HaversineDistanceMeters(
                        point.Latitude, point.Longitude,
                        advisory.Latitude, advisory.Longitude);

                    if (distanceMeters <= advisory.RadiusMeters)
                    {
                        matched.Add(advisory);
                    }
                }
            }

            if (matched.Count == 0)
            {
                return new SafetyCheckResponseDto
                {
                    OverallSafetyLevel = SafetyLevel.Safe,
                    Reason = "No active safety advisories found along this route/area.",
                    MatchedAdvisories = []
                };
            }

            // Worst level wins: Danger > Caution > Safe
            var worst = matched.OrderByDescending(a => (int)a.SafetyLevel).First();

            return new SafetyCheckResponseDto
            {
                OverallSafetyLevel = worst.SafetyLevel,
                Reason = worst.Reason,
                MatchedAdvisories = [.. matched.Select(ToDto)]
            };
        }

        private async Task<List<TravelAdvisory>> GetActiveAdvisoryEntitiesAsync()
        {
            var now = DateTime.UtcNow;
            return await _db.TravelAdvisories
                .Where(a => a.ExpiresAt == null || a.ExpiresAt > now)
                .ToListAsync();
        }

        // Standard great-circle distance formula between two lat/lng points, in meters.
        private static double HaversineDistanceMeters(double lat1, double lon1, double lat2, double lon2)
        {
            const double earthRadiusMeters = 6371000;
            double dLat = DegreesToRadians(lat2 - lat1);
            double dLon = DegreesToRadians(lon2 - lon1);

            double a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                       Math.Cos(DegreesToRadians(lat1)) * Math.Cos(DegreesToRadians(lat2)) *
                       Math.Sin(dLon / 2) * Math.Sin(dLon / 2);

            double c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
            return earthRadiusMeters * c;
        }

        private static double DegreesToRadians(double degrees) => degrees * Math.PI / 180.0;

        private static TravelAdvisoryResponseDto ToDto(TravelAdvisory entity) => new()
        {
            Id = entity.Id,
            AreaName = entity.AreaName,
            Latitude = entity.Latitude,
            Longitude = entity.Longitude,
            RadiusMeters = entity.RadiusMeters,
            SafetyLevel = entity.SafetyLevel,
            Reason = entity.Reason,
            CreatedAt = entity.CreatedAt,
            ExpiresAt = entity.ExpiresAt
        };
    }
}
