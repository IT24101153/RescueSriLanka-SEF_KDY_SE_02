namespace RescueSriLanka.Api.Models
{
    // Overall availability of a rescue team
    public enum TeamStatus
    {
        Available,
        OnMission,
        OffDuty
    }

    // Skill/specialization tags — used for skill-based matching.
    // Add more as your group's scope requires.
    public enum SkillType
    {
        WaterRescue,
        FirstAid,
        Paramedic,
        StructuralCollapse,
        FireResponse,
        Logistics,
        Driving
    }

    public enum VehicleType
    {
        Ambulance,
        Boat,
        FireTruck,
        FourByFour,
        Truck
    }

    public enum VehicleStatus
    {
        Available,
        InUse,
        UnderMaintenance
    }

    // The core status workflow for a Dispatch — this is your
    // "business-specific operation": dispatch status workflow.
    public enum DispatchStatus
    {
        Pending,     // created, waiting to be sent out
        Dispatched,  // team has been notified/sent
        EnRoute,     // team is travelling to the location
        OnScene,     // team has arrived
        Resolved,    // task completed
        Cancelled    // dispatch called off
    }

    // Mirrors the AI-proposed-plan approval flow described in the proposal
    public enum ApprovalStatus
    {
        PendingApproval,
        Approved,
        Rejected,
        Revised
    }
}
