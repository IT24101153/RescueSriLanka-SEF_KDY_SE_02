using Microsoft.EntityFrameworkCore;
using RescueSriLanka.Api.Models;

namespace RescueSriLanka.Api.Data;

/// <summary>
/// The single EF Core context for the platform. Every component's entities are
/// registered here — add new DbSets alongside <see cref="Users"/>.
/// </summary>
public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();

    // ---- Component A — Incident & Disaster Map ----
    public DbSet<Incident> Incidents => Set<Incident>();
    public DbSet<IncidentImage> IncidentImages => Set<IncidentImage>();
    public DbSet<SafetyZone> SafetyZones => Set<SafetyZone>();

    // ---- Agentic AI workflow state (shared by all four agents) ----
    public DbSet<AgentRun> AgentRuns => Set<AgentRun>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<User>(entity =>
        {
            entity.ToTable("users");

            entity.HasIndex(user => user.Email).IsUnique();

            // Persist the role as readable text rather than an opaque integer.
            entity.Property(user => user.Role)
                .HasConversion<string>()
                .HasMaxLength(40);
        });

        modelBuilder.Entity<Incident>(entity =>
        {
            entity.ToTable("incidents");

            // Enums as text keeps the table readable in the Supabase editor.
            entity.Property(incident => incident.Type).HasConversion<string>().HasMaxLength(40);
            entity.Property(incident => incident.Severity).HasConversion<string>().HasMaxLength(40);
            entity.Property(incident => incident.Status).HasConversion<string>().HasMaxLength(40);
            entity.Property(incident => incident.AiSeverity).HasConversion<string>().HasMaxLength(40);

            // The map and dashboard filter on these constantly.
            entity.HasIndex(incident => incident.Status);
            entity.HasIndex(incident => incident.Severity);
            entity.HasIndex(incident => incident.IsActive);
            entity.HasIndex(incident => incident.District);
            entity.HasIndex(incident => new { incident.Latitude, incident.Longitude });

            entity.HasMany(incident => incident.Images)
                .WithOne(image => image.Incident)
                .HasForeignKey(image => image.IncidentId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<IncidentImage>(entity => entity.ToTable("incident_images"));

        modelBuilder.Entity<AgentRun>(entity =>
        {
            entity.ToTable("agent_runs");

            entity.Property(run => run.Status).HasConversion<string>().HasMaxLength(40);

            entity.HasIndex(run => run.AgentName);
            entity.HasIndex(run => run.IncidentId);
            entity.HasIndex(run => run.StartedAt);
        });

        modelBuilder.Entity<SafetyZone>(entity =>
        {
            entity.ToTable("safety_zones");

            entity.Property(zone => zone.Status).HasConversion<string>().HasMaxLength(40);
            entity.Property(zone => zone.Source).HasConversion<string>().HasMaxLength(40);

            entity.HasIndex(zone => zone.Status);
            entity.HasIndex(zone => zone.IsActive);

            // A derived zone disappears with the incident that produced it.
            entity.HasOne(zone => zone.SourceIncident)
                .WithMany()
                .HasForeignKey(zone => zone.SourceIncidentId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
