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

    // Whether a citizen-submitted report has been checked by an admin/coordinator.
    // Source: leader's clarification — admin verifies each report as real or fake
    // before it's treated as legitimate (e.g. before it can appear on Student A's map).
    public enum VerificationStatus
    {
        PendingVerification,
        Verified,
        RejectedFake
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