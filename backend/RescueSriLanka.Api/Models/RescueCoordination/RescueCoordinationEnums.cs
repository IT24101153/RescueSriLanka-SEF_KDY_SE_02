namespace RescueSriLanka.Api.Models;

// Component D persisted enum values. Do not reorder existing members.
public enum TeamStatus
{
    Available = 0,
    OnMission = 1,
    OffDuty = 2
}

public enum SkillType
{
    WaterRescue = 0,
    FirstAid = 1,
    Paramedic = 2,
    StructuralCollapse = 3,
    FireResponse = 4,
    Logistics = 5,
    Driving = 6
}

public enum VehicleType
{
    Ambulance = 0,
    Boat = 1,
    FireTruck = 2,
    FourByFour = 3,
    Truck = 4
}

public enum VehicleStatus
{
    Available = 0,
    InUse = 1,
    UnderMaintenance = 2
}

public enum DispatchStatus
{
    Pending = 0,
    Dispatched = 1,
    EnRoute = 2,
    OnScene = 3,
    Resolved = 4,
    Cancelled = 5
}

public enum ApprovalStatus
{
    PendingApproval = 0,
    Approved = 1,
    Rejected = 2,
    Revised = 3
}

public enum AssignmentStatus
{
    Proposed = 0,
    PendingApproval = 1,
    Approved = 2,
    Rejected = 3
}
