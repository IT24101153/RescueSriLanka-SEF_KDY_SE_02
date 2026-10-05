using Microsoft.EntityFrameworkCore;
using RescueSriLanka.Api.Data;
using RescueSriLanka.Api.Features.ComponentB.Agents.PlannerAgent;
using RescueSriLanka.Api.Features.ComponentB.DTOs;
using RescueSriLanka.Api.Features.ComponentB.Models;
using RescueSriLanka.Api.Models;

namespace RescueSriLanka.Api.Features.ComponentB.Services
{
    public interface IEmergencyContactService
    {
        // Coordinates win over the fallback district: a request is about where help
        // is needed, which is not always the district on the requester's profile.
        Task<EmergencyContactsResponseDto> GetForAreaAsync(
            double? latitude, double? longitude, string? fallbackDistrict, CancellationToken ct = default);
    }

    public class EmergencyContactService(AppDbContext db) : IEmergencyContactService
    {
        // A district unit further than this from the request is not "local" to it.
        internal const double MaxMatchDistanceKm = 60;

        public async Task<EmergencyContactsResponseDto> GetForAreaAsync(
            double? latitude, double? longitude, string? fallbackDistrict, CancellationToken ct = default)
        {
            var contacts = await db.EmergencyContacts.AsNoTracking()
                .Where(c => c.IsActive)
                .ToListAsync(ct);

            var districtUnits = contacts.Where(c => c.District is not null).ToList();
            var matched = MatchByLocation(districtUnits, latitude, longitude)
                ?? MatchByName(districtUnits, fallbackDistrict);

            return new EmergencyContactsResponseDto
            {
                MatchedDistrict = matched?.District,
                Contacts = [.. contacts
                    .Where(c => c.District is null || c.Id == matched?.Id)
                    .OrderBy(c => c.Category == EmergencyContactCategory.DistrictDisaster ? -1 : (int)c.Category)
                    .ThenBy(c => c.SortOrder)
                    .Select(ToDto)]
            };
        }

        private static EmergencyContact? MatchByLocation(
            List<EmergencyContact> units, double? latitude, double? longitude)
        {
            if (latitude is null || longitude is null) return null;

            return units
                .Where(u => u.Latitude is not null && u.Longitude is not null)
                .Select(u => (Unit: u, Km: Geo.DistanceKm(latitude.Value, longitude.Value, u.Latitude!.Value, u.Longitude!.Value)))
                .Where(x => x.Km <= MaxMatchDistanceKm)
                .OrderBy(x => x.Km)
                .Select(x => x.Unit)
                .FirstOrDefault();
        }

        private static EmergencyContact? MatchByName(List<EmergencyContact> units, string? district)
        {
            var name = SriLankaDistricts.Normalise(district);
            return name is null
                ? null
                : units.FirstOrDefault(u => string.Equals(u.District, name, StringComparison.OrdinalIgnoreCase));
        }

        private static EmergencyContactDto ToDto(EmergencyContact c) => new()
        {
            Id = c.Id,
            Name = c.Name,
            Category = c.Category,
            PhoneNumber = c.PhoneNumber,
            SecondaryPhoneNumber = c.SecondaryPhoneNumber,
            Description = c.Description,
            District = c.District,
            IsAvailable24x7 = c.IsAvailable24x7
        };
    }
}
