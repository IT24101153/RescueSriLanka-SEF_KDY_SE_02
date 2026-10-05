using RescueSriLanka.Api.Features.ComponentB.Models;

namespace RescueSriLanka.Api.Features.ComponentB.DTOs
{
    public class EmergencyContactDto
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public EmergencyContactCategory Category { get; set; }
        public string PhoneNumber { get; set; } = string.Empty;
        public string? SecondaryPhoneNumber { get; set; }
        public string? Description { get; set; }
        public string? District { get; set; }
        public bool IsAvailable24x7 { get; set; }
    }

    // The numbers that apply to one place: the national ones plus the district
    // disaster-management unit, when one is known for the area.
    public class EmergencyContactsResponseDto
    {
        // The district the local unit was matched for; null when none matched,
        // in which case only the national numbers are returned.
        public string? MatchedDistrict { get; set; }
        public List<EmergencyContactDto> Contacts { get; set; } = [];
    }
}
