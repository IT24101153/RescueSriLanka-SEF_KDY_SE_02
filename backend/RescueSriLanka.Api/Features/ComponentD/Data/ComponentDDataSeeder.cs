using System.Data;
using Microsoft.EntityFrameworkCore;
using RescueSriLanka.Api.Data;
using RescueSriLanka.Api.Features.ComponentD.Models;

namespace RescueSriLanka.Api.Features.ComponentD.Data;

public static class ComponentDDataSeeder
{
    // Reserved fictional fixture IDs. Existing rows are never updated or deleted.
    public static readonly Guid TeamId = Guid.Parse("d0000000-0000-4000-8000-000000000001");
    public static readonly Guid MemberId = Guid.Parse("d0000000-0000-4000-8000-000000000002");
    public static readonly Guid DriverId = Guid.Parse("d0000000-0000-4000-8000-000000000003");
    public static readonly Guid VehicleId = Guid.Parse("d0000000-0000-4000-8000-000000000004");
    private static readonly Guid HistoryTeamId = Guid.Parse("d0000000-0000-4000-8000-000000000005");
    private static readonly Guid HistoryMemberId = Guid.Parse("d0000000-0000-4000-8000-000000000006");
    private static readonly Guid HistoryVehicleId = Guid.Parse("d0000000-0000-4000-8000-000000000007");
    public static readonly Guid AssignmentId = Guid.Parse("d0000000-0000-4000-8000-000000000008");
    public static readonly Guid DispatchId = Guid.Parse("d0000000-0000-4000-8000-000000000009");

    public static async Task SeedAsync(ComponentDDbContext db, AppDbContext sharedDb,
        IHostEnvironment environment, IConfiguration configuration, ILogger logger,
        CancellationToken ct = default)
    {
        if (!environment.IsDevelopment() || !configuration.GetValue("ComponentD:SeedDemoData", false))
            return;

        // PostgreSQL uses one atomic transaction. InMemory is supported only for isolated tests.
        await using var transaction = db.Database.IsRelational()
            ? await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct) : null;

        // Treat any occupied fixture ID conservatively: never attach new children to an operator row.
        var baseExists = await db.RescueTeams.AnyAsync(t => t.Id == TeamId, ct)
            || await db.TeamMembers.AnyAsync(m => m.Id == MemberId || m.Id == DriverId, ct)
            || await db.Vehicles.AnyAsync(v => v.Id == VehicleId, ct);
        if (!baseExists)
        {
            db.RescueTeams.Add(new RescueTeam { Id = TeamId, Name = "DEMO - Fictional River Rescue", Status = TeamStatus.Available });
            db.TeamMembers.AddRange(
                new TeamMember { Id = MemberId, RescueTeamId = TeamId, FullName = "DEMO Rescuer One", Phone = "0000000001", Skill = SkillType.WaterRescue },
                new TeamMember { Id = DriverId, RescueTeamId = TeamId, FullName = "DEMO Driver Two", Phone = "0000000002", Skill = SkillType.Driving });
            db.Vehicles.Add(new Vehicle { Id = VehicleId, RescueTeamId = TeamId, PlateNumber = "DEMO-BOAT-01", Type = VehicleType.Boat, Capacity = 6, Status = VehicleStatus.Available });
        }
        else
            logger.LogInformation("Component D base fixture IDs already occupied; existing rows left unchanged.");

        var reference = configuration["ComponentD:DemoReferenceIncidentId"];
        if (!Guid.TryParse(reference, out var incidentId)
            || !await sharedDb.Incidents.AsNoTracking().AnyAsync(i => i.Id == incidentId, ct))
        {
            logger.LogInformation("Component D demo incident is missing or not found; historical assignment/dispatch skipped.");
        }
        else
        {
            var historyExists = await db.RescueTeams.AnyAsync(t => t.Id == HistoryTeamId, ct)
                || await db.TeamMembers.AnyAsync(m => m.Id == HistoryMemberId, ct)
                || await db.Vehicles.AnyAsync(v => v.Id == HistoryVehicleId, ct)
                || await db.Assignments.AnyAsync(a => a.Id == AssignmentId, ct)
                || await db.Dispatches.AnyAsync(d => d.Id == DispatchId, ct);
            if (!historyExists)
            {
                // Separate historical resources keep the base graph free of assignment reservations.
                var time = new DateTime(2026, 1, 1, 8, 0, 0, DateTimeKind.Utc);
                db.RescueTeams.Add(new RescueTeam { Id = HistoryTeamId, Name = "DEMO - Fictional Historical Team", Status = TeamStatus.Available });
                db.TeamMembers.Add(new TeamMember { Id = HistoryMemberId, RescueTeamId = HistoryTeamId, FullName = "DEMO Medic Three", Phone = "0000000003", Skill = SkillType.FirstAid });
                db.Vehicles.Add(new Vehicle { Id = HistoryVehicleId, RescueTeamId = HistoryTeamId, PlateNumber = "DEMO-AMB-01", Type = VehicleType.Ambulance, Capacity = 2, Status = VehicleStatus.Available });
                db.Assignments.Add(new Assignment { Id = AssignmentId, IncidentId = incidentId, RescueTeamId = HistoryTeamId, VehicleId = HistoryVehicleId, RequiredSkill = SkillType.FirstAid, RequiredCapacity = 1, Status = AssignmentStatus.Approved, AssignedAt = time, Notes = "DEMO fictional historical fixture; not a real incident response." });
                db.Dispatches.Add(new Dispatch { Id = DispatchId, AssignmentId = AssignmentId, Status = DispatchStatus.Resolved, ApprovalStatus = ApprovalStatus.Approved, ApprovedByUserId = null, ApprovedAt = time.AddMinutes(1), DispatchedAt = time.AddMinutes(2), EnRouteAt = time.AddMinutes(3), OnSceneAt = time.AddMinutes(10), ResolvedAt = time.AddMinutes(30), Notes = "DEMO terminal fixture; no real coordinator approval claimed." });
            }
            else
                logger.LogInformation("Component D historical fixture IDs already occupied; existing rows left unchanged.");
        }

        await db.SaveChangesAsync(ct);
        if (transaction is not null) await transaction.CommitAsync(ct);
    }
}
