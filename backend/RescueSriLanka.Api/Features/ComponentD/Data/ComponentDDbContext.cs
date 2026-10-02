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

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        StampAuditFields();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(
        bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        StampAuditFields();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    private void StampAuditFields()
    {
        var now = DateTime.UtcNow;
        foreach (var entry in ChangeTracker.Entries<IComponentDAuditable>())
        {
            if (entry.State == EntityState.Added)
            {
                entry.Entity.CreatedAt = now;
                entry.Entity.UpdatedAt = now;
            }
            else if (entry.State == EntityState.Modified)
            {
                var created = entry.Property(e => e.CreatedAt);
                created.CurrentValue = created.OriginalValue;
                created.IsModified = false;
                entry.Entity.UpdatedAt = now;
            }
        }
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        ConfigureAudit<RescueTeam>(modelBuilder);
        ConfigureAudit<TeamMember>(modelBuilder);
        ConfigureAudit<Vehicle>(modelBuilder);
        modelBuilder.Entity<Vehicle>().HasIndex(v => v.PlateNumber).IsUnique();
        ConfigureAudit<Assignment>(modelBuilder);
        ConfigureAudit<Dispatch>(modelBuilder);

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
    private static void ConfigureAudit<TEntity>(ModelBuilder modelBuilder)
        where TEntity : class, IComponentDAuditable
    {
        var entity = modelBuilder.Entity<TEntity>();
        entity.Property(e => e.CreatedAt).IsRequired()
            .HasColumnType("timestamp with time zone").HasDefaultValueSql("CURRENT_TIMESTAMP");
        entity.Property(e => e.UpdatedAt).IsRequired()
            .HasColumnType("timestamp with time zone").HasDefaultValueSql("CURRENT_TIMESTAMP");
    }

}
