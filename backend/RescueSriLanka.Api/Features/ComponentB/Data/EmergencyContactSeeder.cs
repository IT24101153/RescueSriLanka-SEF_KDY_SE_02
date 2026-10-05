using Microsoft.EntityFrameworkCore;
using RescueSriLanka.Api.Data;
using RescueSriLanka.Api.Features.ComponentB.Models;

namespace RescueSriLanka.Api.Features.ComponentB.Data;

/// <summary>
/// Sri Lanka's public emergency numbers. Unlike the demo fixtures these are
/// real reference data, so they are added wherever the API starts. Existing
/// rows are matched by name and left alone, so an admin's edits survive a restart.
/// </summary>
public static class EmergencyContactSeeder
{
    public static async Task SeedAsync(AppDbContext db, CancellationToken ct = default)
    {
        var existing = (await db.EmergencyContacts.Select(c => c.Name).ToListAsync(ct)).ToHashSet();
        var missing = Contacts().Where(c => !existing.Contains(c.Name)).ToList();
        if (missing.Count == 0) return;

        db.EmergencyContacts.AddRange(missing);
        await db.SaveChangesAsync(ct);
    }

    internal static List<EmergencyContact> Contacts()
    {
        var order = 0;

        EmergencyContact National(
            EmergencyContactCategory category, string name, string phone, string description,
            string? secondary = null, bool always = false) => new()
        {
            Name = name,
            Category = category,
            PhoneNumber = phone,
            SecondaryPhoneNumber = secondary,
            Description = description,
            IsAvailable24x7 = always,
            SortOrder = order++
        };

        EmergencyContact District(string district, string phone, string secondary, double lat, double lng) => new()
        {
            Name = $"{district} District Disaster Management Unit",
            Category = EmergencyContactCategory.DistrictDisaster,
            PhoneNumber = phone,
            SecondaryPhoneNumber = secondary,
            Description = "Local evacuation and boats",
            District = district,
            Latitude = lat,
            Longitude = lng,
            SortOrder = order++
        };

        return
        [
            // Central disaster rescue
            National(EmergencyContactCategory.CentralRescue, "Disaster Management Centre", "117", "National disaster hotline", always: true),
            National(EmergencyContactCategory.CentralRescue, "Sri Lanka Navy", "105", "Emergency and flood rescue"),
            National(EmergencyContactCategory.CentralRescue, "Sri Lanka Air Force", "116", "Air and helicopter rescue"),
            National(EmergencyContactCategory.CentralRescue, "Army Headquarters", "113", "Disaster relief"),
            National(EmergencyContactCategory.CentralRescue, "Sri Lanka Coast Guard", "106", "Coastal and sea rescue"),

            // Medical and fire
            National(EmergencyContactCategory.Medical, "Suwa Seriya Ambulance", "1990", "Free ambulance service", always: true),
            National(EmergencyContactCategory.Medical, "National Hospital Accident Service", "011-2691111", "Colombo"),
            National(EmergencyContactCategory.Fire, "Fire and Rescue Service", "110", "Fire and rescue", always: true),

            // Police and authorities
            National(EmergencyContactCategory.Police, "Police Emergency Hotline and National Help Desk", "119", "Police emergency hotline and national help desk", secondary: "118", always: true),
            National(EmergencyContactCategory.Authority, "Department of Meteorology", "011-2686686", "Weather updates"),
            National(EmergencyContactCategory.Authority, "National Building Research Organisation (NBRO)", "011-2588946", "Landslide risk"),

            // District disaster management units — coordinates are each district's main town.
            District("Colombo", "011-2434028", "077-3957870", 6.9271, 79.8612),
            District("Gampaha", "033-2234670", "077-3957871", 7.0840, 80.0098),
            District("Kalutara", "034-2222912", "077-3957872", 6.5854, 79.9607),
            District("Kandy", "081-2202697", "077-3957877", 7.2906, 80.6337),
            District("Galle", "091-2227315", "077-3957873", 6.0535, 80.2210),
            District("Ratnapura", "045-2222991", "077-3957876", 6.6828, 80.3992)
        ];
    }
}
