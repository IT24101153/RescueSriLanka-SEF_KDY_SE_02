using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using RescueSriLanka.Api.Data;
using RescueSriLanka.Api.DTOs;
using RescueSriLanka.Api.Models;

namespace RescueSriLanka.Api.Agents.SafetyValidation;

// The only factual boundary exposed to the Gemini safety agent. Every method
// is read-only and derives operational facts from Component D's database.
public sealed class SafetyValidationTools
{
    private readonly ComponentDDbContext _db;

    public SafetyValidationTools(ComponentDDbContext db) => _db = db;

    public static readonly string[] AllowedToolNames =
    [
        "get_assignment_context",
        "check_team_availability",
        "check_required_skill",
        "check_team_conflict",
        "check_vehicle_availability",
        "check_vehicle_capacity",
        "check_vehicle_conflict"
    ];

    public async Task<AssignmentValidationContextDto?> GetAssignmentContextAsync(Guid assignmentId)
    {
        var assignment = await _db.Assignments.AsNoTracking()
            .FirstOrDefaultAsync(a => a.Id == assignmentId);
        return assignment is null ? null : ToContext(assignment);
    }

    public async Task<IReadOnlyList<SafetyValidationCheckDto>> RunMandatoryChecksAsync(
        Guid assignmentId, int expectedPlanVersion)
    {
        var assignment = await _db.Assignments.AsNoTracking()
            .FirstOrDefaultAsync(a => a.Id == assignmentId);
        if (assignment is null)
        {
            return
            [
                new("ASSIGNMENT_EXISTS", false, "Assignment was not found."),
                new("PLAN_VERSION_MATCHES", false, "Assignment is unavailable for plan-version validation."),
                new("TEAM_AVAILABLE", false, "Assignment is unavailable."),
                new("REQUIRED_SKILL_PRESENT", false, "Assignment is unavailable."),
                new("TEAM_CONFLICT", false, "Assignment is unavailable."),
                new("VEHICLE_EXISTS", false, "Assignment is unavailable."),
                new("VEHICLE_OWNERSHIP", false, "Assignment is unavailable."),
                new("VEHICLE_AVAILABLE", false, "Assignment is unavailable."),
                new("VEHICLE_CAPACITY", false, "Assignment is unavailable."),
                new("VEHICLE_CONFLICT", false, "Assignment is unavailable.")
            ];
        }

        var team = await _db.RescueTeams.AsNoTracking()
            .Include(t => t.Members)
            .FirstOrDefaultAsync(t => t.Id == assignment.RescueTeamId);
        var vehicle = assignment.VehicleId.HasValue
            ? await _db.Vehicles.AsNoTracking().FirstOrDefaultAsync(v => v.Id == assignment.VehicleId.Value)
            : null;

        var hasTeamConflict = await HasActiveTeamConflictAsync(assignment.Id, assignment.RescueTeamId);
        var hasVehicleConflict = assignment.VehicleId.HasValue
            && await HasActiveVehicleConflictAsync(assignment.Id, assignment.VehicleId.Value);

        return
        [
            new("ASSIGNMENT_EXISTS", true, "Assignment exists."),
            new("PLAN_VERSION_MATCHES", assignment.PlanVersion == expectedPlanVersion,
                assignment.PlanVersion == expectedPlanVersion ? "Plan version matches." : "Assignment plan version changed."),
            new("TEAM_AVAILABLE", team?.Status == TeamStatus.Available,
                team is null ? "Rescue team was not found." : team.Status == TeamStatus.Available ? "Rescue team is available." : "Rescue team is not available."),
            new("REQUIRED_SKILL_PRESENT", team?.Members.Any(m => m.IsAvailable && m.Skill == assignment.RequiredSkill) == true,
                team is null
                    ? "Rescue team was not found."
                    : team.Members.Any(m => m.IsAvailable && m.Skill == assignment.RequiredSkill)
                        ? "An available member has the required skill."
                        : "No available member has the required skill."),
            new("TEAM_CONFLICT", !hasTeamConflict,
                hasTeamConflict ? "Rescue team has another active assignment or dispatch." : "No active rescue-team conflict."),
            new("VEHICLE_EXISTS", vehicle is not null,
                vehicle is null ? "Selected vehicle was not found." : "Selected vehicle exists."),
            new("VEHICLE_OWNERSHIP", vehicle?.RescueTeamId == assignment.RescueTeamId,
                vehicle is null ? "Selected vehicle was not found." : vehicle.RescueTeamId == assignment.RescueTeamId ? "Vehicle belongs to the selected rescue team." : "Vehicle belongs to another rescue team."),
            new("VEHICLE_AVAILABLE", vehicle?.Status == VehicleStatus.Available,
                vehicle is null ? "Selected vehicle was not found." : vehicle.Status == VehicleStatus.Available ? "Vehicle is available." : "Vehicle is not available."),
            new("VEHICLE_CAPACITY", vehicle is not null && vehicle.Capacity >= assignment.RequiredCapacity,
                vehicle is null ? "Selected vehicle was not found." : vehicle.Capacity >= assignment.RequiredCapacity ? "Vehicle capacity meets the requirement." : "Vehicle capacity is insufficient."),
            new("VEHICLE_CONFLICT", !hasVehicleConflict,
                hasVehicleConflict ? "Vehicle has another active assignment or dispatch." : "No active vehicle conflict.")
        ];
    }

    public object[] GetGeminiToolSchemas() => AllowedToolNames.Select(name => (object)new
    {
        type = "function",
        name,
        description = $"Read-only deterministic safety check: {name}.",
        parameters = new
        {
            type = "object",
            properties = new
            {
                assignmentId = new { type = "string", description = "The assignment GUID supplied by the application." },
                planVersion = new { type = "integer", description = "The assignment plan version supplied by the application." }
            },
            required = new[] { "assignmentId", "planVersion" }
        }
    }).ToArray();

    public async Task<SafetyValidationCheckDto?> ExecuteAsync(
        string toolName, JsonElement arguments, Guid expectedAssignmentId, int expectedPlanVersion)
    {
        if (!AllowedToolNames.Contains(toolName, StringComparer.Ordinal)
            || !TryValidateArguments(arguments, expectedAssignmentId, expectedPlanVersion))
            return null;

        if (toolName == "get_assignment_context")
        {
            var context = await GetAssignmentContextAsync(expectedAssignmentId);
            return new SafetyValidationCheckDto(
                "ASSIGNMENT_EXISTS",
                context is not null,
                context is null ? "Assignment was not found." : "Trusted assignment context retrieved.",
                context);
        }

        var checks = await RunMandatoryChecksAsync(expectedAssignmentId, expectedPlanVersion);
        return toolName switch
        {
            "check_team_availability" => checks.First(c => c.Name == "TEAM_AVAILABLE"),
            "check_required_skill" => checks.First(c => c.Name == "REQUIRED_SKILL_PRESENT"),
            "check_team_conflict" => checks.First(c => c.Name == "TEAM_CONFLICT"),
            "check_vehicle_availability" => checks.First(c => c.Name == "VEHICLE_AVAILABLE"),
            "check_vehicle_capacity" => checks.First(c => c.Name == "VEHICLE_CAPACITY"),
            "check_vehicle_conflict" => checks.First(c => c.Name == "VEHICLE_CONFLICT"),
            _ => null
        };
    }

    private async Task<bool> HasActiveTeamConflictAsync(Guid assignmentId, Guid teamId) =>
        await _db.Assignments.AsNoTracking().AnyAsync(a =>
            a.Id != assignmentId && a.RescueTeamId == teamId && a.Status != AssignmentStatus.Rejected &&
            (a.Dispatch == null || (a.Dispatch.Status != DispatchStatus.Resolved && a.Dispatch.Status != DispatchStatus.Cancelled)));

    private async Task<bool> HasActiveVehicleConflictAsync(Guid assignmentId, Guid vehicleId) =>
        await _db.Assignments.AsNoTracking().AnyAsync(a =>
            a.Id != assignmentId && a.VehicleId == vehicleId && a.Status != AssignmentStatus.Rejected &&
            (a.Dispatch == null || (a.Dispatch.Status != DispatchStatus.Resolved && a.Dispatch.Status != DispatchStatus.Cancelled)));

    private static bool TryValidateArguments(JsonElement arguments, Guid assignmentId, int planVersion) =>
        arguments.ValueKind == JsonValueKind.Object
        && arguments.TryGetProperty("assignmentId", out var id)
        && Guid.TryParse(id.GetString(), out var suppliedId)
        && suppliedId == assignmentId
        && arguments.TryGetProperty("planVersion", out var version)
        && version.TryGetInt32(out var suppliedVersion)
        && suppliedVersion == planVersion;

    private static AssignmentValidationContextDto ToContext(Assignment assignment) => new(
        assignment.Id, assignment.PlanVersion, assignment.RescueTeamId, assignment.VehicleId,
        assignment.RequiredSkill, assignment.RequiredCapacity, assignment.Status,
        assignment.IncidentId, assignment.HelpRequestId);
}
