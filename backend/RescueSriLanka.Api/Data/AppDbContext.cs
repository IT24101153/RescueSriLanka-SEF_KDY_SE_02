using Microsoft.EntityFrameworkCore;
using RescueSriLanka.Api.Models;
using RescueSriLanka.Api.Features.ComponentA.Models;
using RescueSriLanka.Api.Features.ComponentB.Models;
using RescueSriLanka.Api.Features.ComponentC.Models;
// Components B and C each define a HelpRequest. B's is the citizen help
// request; C's is a request for resources, kept in its own table.
using HelpRequest = RescueSriLanka.Api.Features.ComponentB.Models.HelpRequest;
using ResourceHelpRequest = RescueSriLanka.Api.Features.ComponentC.Models.HelpRequest;

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

    // ---- Component B — Help Requests & Travel Advisories ----
    public DbSet<HelpRequest> HelpRequests => Set<HelpRequest>();
    public DbSet<RequestStatusHistory> RequestStatusHistories => Set<RequestStatusHistory>();
    public DbSet<TravelAdvisory> TravelAdvisories => Set<TravelAdvisory>();

    // ---- Agentic AI workflow state (shared by all four agents) ----
    public DbSet<AgentRun> AgentRuns => Set<AgentRun>();

    // Planner Agent workflows (Component B) — dedicated tables, jsonb columns.
    public DbSet<AgentWorkflow> AgentWorkflows => Set<AgentWorkflow>();
    public DbSet<AgentStep> AgentSteps => Set<AgentStep>();

    // ---- Component C — Resource Management ----
    public DbSet<Shelter> Shelters => Set<Shelter>();
    public DbSet<MedicalSupply> MedicalSupplies => Set<MedicalSupply>();
    public DbSet<FoodWaterStock> FoodWaterStocks => Set<FoodWaterStock>();
    public DbSet<ResourceAllocation> ResourceAllocations => Set<ResourceAllocation>();
    public DbSet<Donation> Donations => Set<Donation>();

    /// <summary>Requests for resources (Component C), separate from Component
    /// B's citizen help requests.</summary>
    public DbSet<ResourceHelpRequest> ResourceHelpRequests => Set<ResourceHelpRequest>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<User>(entity =>
        {
            entity.ToTable("users");

            entity.HasIndex(user => user.Email).IsUnique();

            // Every district warning asks "who lives here?" — this is the index
            // that question runs on.
            entity.HasIndex(user => user.District);

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

        // ---- Component B ----

        // Explicit relationship: one HelpRequest has many RequestStatusHistory entries
        modelBuilder.Entity<RequestStatusHistory>()
            .HasOne(h => h.HelpRequest)
            .WithMany(r => r.StatusHistory)
            .HasForeignKey(h => h.HelpRequestId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<HelpRequest>(entity =>
        {
            entity.HasOne<User>()
                .WithMany()
                .HasForeignKey(request => request.CitizenId)
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasOne<Incident>()
                .WithMany()
                .HasForeignKey(request => request.RelatedIncidentId)
                .OnDelete(DeleteBehavior.SetNull);
            entity.Property(request => request.Type).HasConversion<string>().HasMaxLength(24);
            entity.Property(request => request.VerificationStatus).HasConversion<string>().HasMaxLength(32);
            entity.Property(request => request.Description).HasMaxLength(2000).IsRequired();
            entity.Property(request => request.VerificationNotes).HasMaxLength(1000);
            entity.Property(request => request.ImageUrl).HasMaxLength(2048);
            entity.HasIndex(request => new { request.CitizenId, request.CreatedAt });
            entity.HasIndex(request => new { request.Status, request.UrgencyScore });
            entity.HasIndex(request => request.VerificationStatus);
            entity.ToTable(table =>
            {
                table.HasCheckConstraint("CK_HelpRequests_UrgencyScore", "\"UrgencyScore\" >= 0 AND \"UrgencyScore\" <= 100");
                table.HasCheckConstraint("CK_HelpRequests_Latitude", "\"Latitude\" >= -90 AND \"Latitude\" <= 90");
                table.HasCheckConstraint("CK_HelpRequests_Longitude", "\"Longitude\" >= -180 AND \"Longitude\" <= 180");
            });
        });

        modelBuilder.Entity<RequestStatusHistory>(entity =>
        {
            entity.Property(history => history.Notes).HasMaxLength(1000);
            entity.HasIndex(history => new { history.HelpRequestId, history.ChangedAt });
        });

        modelBuilder.Entity<TravelAdvisory>(entity =>
        {
            entity.Property(advisory => advisory.AreaName).HasMaxLength(200).IsRequired();
            entity.Property(advisory => advisory.Reason).HasMaxLength(1000).IsRequired();
            entity.Property(advisory => advisory.SafetyLevel).HasConversion<string>().HasMaxLength(16);
            entity.HasIndex(advisory => advisory.ExpiresAt);
            entity.ToTable(table =>
            {
                table.HasCheckConstraint("CK_TravelAdvisories_RadiusMeters", "\"RadiusMeters\" > 0 AND \"RadiusMeters\" <= 100000");
                table.HasCheckConstraint("CK_TravelAdvisories_Latitude", "\"Latitude\" >= -90 AND \"Latitude\" <= 90");
                table.HasCheckConstraint("CK_TravelAdvisories_Longitude", "\"Longitude\" >= -180 AND \"Longitude\" <= 180");
            });
        });

        // The shared PostgreSQL schema stores request statuses as their readable
        // enum names (for example, "Pending"), rather than integer values.
        // Keeping that representation avoids a read failure in Npgsql and makes
        // the value easier to inspect directly in the database.
        modelBuilder.Entity<HelpRequest>()
            .Property(r => r.Status)
            .HasConversion<string>()
            .HasColumnType("character varying");

        // AgentWorkflow <-> AgentStep relationship
        modelBuilder.Entity<AgentStep>()
            .HasOne(s => s.AgentWorkflow)
            .WithMany(w => w.Steps)
            .HasForeignKey(s => s.AgentWorkflowId)
            .OnDelete(DeleteBehavior.Cascade);

        // Map the JSON string properties to real Postgres jsonb columns,
        // per the ADR Seed List's "jsonb columns" requirement.
        modelBuilder.Entity<AgentWorkflow>()
            .Property(w => w.ObjectiveSnapshotJson)
            .HasColumnType("jsonb")
            .HasColumnName("ObjectiveSnapshot");

        modelBuilder.Entity<AgentWorkflow>()
            .Property(w => w.PlanJson)
            .HasColumnType("jsonb")
            .HasColumnName("Plan");

        modelBuilder.Entity<AgentWorkflow>()
            .Property(w => w.FinalOutcomeJson)
            .HasColumnType("jsonb")
            .HasColumnName("FinalOutcome");

        modelBuilder.Entity<AgentWorkflow>()
            .HasIndex(workflow => new { workflow.ObjectiveType, workflow.ObjectiveId, workflow.Status });

        modelBuilder.Entity<AgentStep>()
            .Property(s => s.InputParamsJson)
            .HasColumnType("jsonb")
            .HasColumnName("InputParams");

        modelBuilder.Entity<AgentStep>()
            .Property(s => s.ToolResultJson)
            .HasColumnType("jsonb")
            .HasColumnName("ToolResult");

        modelBuilder.Entity<AgentStep>()
            .Property(s => s.ValidationResultJson)
            .HasColumnType("jsonb")
            .HasColumnName("ValidationResult");

        modelBuilder.Entity<AgentStep>()
            .HasIndex(step => new { step.AgentWorkflowId, step.StepNumber })
            .IsUnique();

        // ---- Component C ----

        modelBuilder.Entity<Shelter>(entity =>
        {
            entity.Property(shelter => shelter.Name).HasMaxLength(200).IsRequired();
            entity.Property(shelter => shelter.Address).HasMaxLength(500).IsRequired();
            entity.Property(shelter => shelter.Latitude).HasPrecision(9, 6);
            entity.Property(shelter => shelter.Longitude).HasPrecision(9, 6);
            entity.Ignore(shelter => shelter.AvailableCapacity);
        });

        modelBuilder.Entity<MedicalSupply>(entity =>
        {
            entity.Property(supply => supply.Name).HasMaxLength(200).IsRequired();
            entity.Property(supply => supply.Unit).HasMaxLength(50).IsRequired();
            entity.Ignore(supply => supply.IsLowStock);
        });

        modelBuilder.Entity<FoodWaterStock>(entity =>
        {
            entity.Property(stock => stock.ItemName).HasMaxLength(200).IsRequired();
            entity.Property(stock => stock.Unit).HasMaxLength(50).IsRequired();
            entity.Property(stock => stock.QuantityOnHand).HasPrecision(12, 2);
            entity.Property(stock => stock.LowStockThreshold).HasPrecision(12, 2);
            entity.Ignore(stock => stock.IsLowStock);
        });

        modelBuilder.Entity<ResourceAllocation>(entity =>
        {
            entity.Property(allocation => allocation.ResourceType).HasMaxLength(50).IsRequired();
            entity.Property(allocation => allocation.Status).HasMaxLength(30).IsRequired();
            entity.HasIndex(allocation => new
            {
                allocation.ResourceType,
                allocation.ResourceId,
                allocation.Status
            });
        });

        modelBuilder.Entity<ResourceHelpRequest>(entity =>
        {
            // Its own table: Component B owns "HelpRequests".
            entity.ToTable("resource_help_requests");

            entity.Property(request => request.RequesterName).HasMaxLength(160).IsRequired();
            entity.Property(request => request.ContactNumber).HasMaxLength(40).IsRequired();
            entity.Property(request => request.NeedType).HasMaxLength(50).IsRequired();
            entity.Property(request => request.Description).HasMaxLength(2000).IsRequired();
            entity.Property(request => request.Status).HasMaxLength(30).IsRequired();
            entity.HasIndex(request => new { request.Status, request.CreatedAtUtc });
        });

        modelBuilder.Entity<Donation>(entity =>
        {
            entity.Property(donation => donation.DonorName).HasMaxLength(160).IsRequired();
            entity.Property(donation => donation.ContactNumber).HasMaxLength(40).IsRequired();
            entity.Property(donation => donation.DonationType).HasMaxLength(80).IsRequired();
            entity.Property(donation => donation.Quantity).HasPrecision(12, 2);
            entity.Property(donation => donation.Unit).HasMaxLength(40).IsRequired();
            entity.Property(donation => donation.Notes).HasMaxLength(1000);
            entity.Property(donation => donation.Status).HasMaxLength(30).IsRequired();
            entity.HasIndex(donation => new { donation.Status, donation.CreatedAtUtc });
        });
    }
}
