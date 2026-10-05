namespace RescueSriLanka.Api.Features.ComponentB.Models
{
    // Declaration order is the order the apps list the groups in.
    public enum EmergencyContactCategory
    {
        CentralRescue,
        Medical,
        Fire,
        Police,
        Authority,
        DistrictDisaster
    }

    // A public emergency number a citizen can be pointed to. District is null for
    // national numbers; a district disaster-management unit carries its district
    // and an approximate centre so a request can be matched to it by location.
    public class EmergencyContact
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public string Name { get; set; } = string.Empty;
        public EmergencyContactCategory Category { get; set; }
        public string PhoneNumber { get; set; } = string.Empty;
        public string? SecondaryPhoneNumber { get; set; }
        public string? Description { get; set; }
        public string? District { get; set; }
        public double? Latitude { get; set; }
        public double? Longitude { get; set; }
        public bool IsAvailable24x7 { get; set; }
        public int SortOrder { get; set; }
        public bool IsActive { get; set; } = true;
    }
}
