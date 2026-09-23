using Microsoft.EntityFrameworkCore;
using RescueSriLanka.Api.Models;
using RescueSriLanka.Api.Features.ComponentD.Models;

namespace RescueSriLanka.Api.Features.ComponentD.Data;
public class ComponentDDbContext(DbContextOptions<ComponentDDbContext> options) : DbContext(options)
{
    // Component D-owned schema
    public DbSet<RescueTeam> RescueTeams => Set<RescueTeam>();
    public DbSet<TeamMember> TeamMembers => Set<TeamMember>();
    public DbSet<Vehicle> Vehicles => Set<Vehicle>();
    public DbSet<Assignment> Assignments => Set<Assignment>();
    public DbSet<Dispatch> Dispatches => Set<Dispatch>();

    // Shared workflow schema, used at runtime but owned by the shared workflow context.
    public DbSet<AgentWorkflow> AgentWorkflows => Set<AgentWorkflow>();
    public DbSet<AgentStep> AgentSteps => Set<AgentStep>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Dispatch>()
            .HasIndex(d => d.AssignmentId)
            .IsUnique();

        modelBuilder.Entity<Assignment>()
            .HasOne(a => a.Vehicle)
            .WithMany(v => v.Assignments)
            .HasForeignKey(a => a.VehicleId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<Assignment>()
            .Property(a => a.RequiredCapacity)
            .HasDefaultValue(1);

        modelBuilder.Entity<Assignment>()
            .Property(a => a.PlanVersion)
            .HasDefaultValue(1);

        modelBuilder.Entity<AgentWorkflow>(entity =>
        {
            entity.ToTable("AgentWorkflows", table => table.ExcludeFromMigrations());
            entity.Property(w => w.ObjectiveSnapshotJson)
                .HasColumnType("jsonb")
                .HasColumnName("ObjectiveSnapshot");
            entity.Property(w => w.PlanJson)
                .HasColumnType("jsonb")
                .HasColumnName("Plan");
            entity.Property(w => w.FinalOutcomeJson)
                .HasColumnType("jsonb")
                .HasColumnName("FinalOutcome");
        });

        modelBuilder.Entity<AgentStep>(entity =>
        {
            entity.ToTable("AgentSteps", table => table.ExcludeFromMigrations());
            entity.Property(s => s.InputParamsJson)
                .HasColumnType("jsonb")
                .HasColumnName("InputParams");
            entity.Property(s => s.ToolResultJson)
                .HasColumnType("jsonb")
                .HasColumnName("ToolResult");
            entity.Property(s => s.ValidationResultJson)
                .HasColumnType("jsonb")
                .HasColumnName("ValidationResult");
            entity.HasOne(s => s.Workflow)
                .WithMany(w => w.Steps)
                .HasForeignKey(s => s.AgentWorkflowId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
