namespace RescueSriLanka.Api.Models
{
    public enum HelpRequestType
    {
        Water,
        Food,
        Medical,
        Rescue,
        Shelter,
        Other
    }

    public enum HelpRequestStatus
    {
        Pending,
        Assigned,
        InProgress,
        Resolved,
        Cancelled
    }

    // NOTE: SafetyLevel is also used by Student A's SafetyZone entity.
    // Coordinate with them so you're not defining two different versions —
    // ideally this lives in a shared Models/Common folder.
    public enum SafetyLevel
    {
        Safe,
        Caution,
        Danger
    }
}