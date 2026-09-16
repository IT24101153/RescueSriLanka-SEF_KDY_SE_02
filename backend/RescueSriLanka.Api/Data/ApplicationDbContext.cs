using Microsoft.EntityFrameworkCore;
using RescueSriLanka.Api.Models;

namespace RescueSriLanka.Api.Data
{
    public class ApplicationDbContext : DbContext
    {
        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
            : base(options)
        {
        }

        // Student B's tables
        public DbSet<HelpRequest> HelpRequests { get; set; }
        public DbSet<RequestStatusHistory> RequestStatusHistories { get; set; }
        public DbSet<TravelAdvisory> TravelAdvisories { get; set; }

        // Agentic AI workflow tables (per ADR Seed List: dedicated tables, jsonb columns)
        public DbSet<AgentWorkflow> AgentWorkflows { get; set; }
        public DbSet<AgentStep> AgentSteps { get; set; }

        // NOTE: as your teammates add their entities, they'll add their own
        // DbSet<> lines here too — this file is shared, so coordinate merges
        // carefully to avoid overwriting each other's DbSets.

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // Explicit relationship: one HelpRequest has many RequestStatusHistory entries
            modelBuilder.Entity<RequestStatusHistory>()
                .HasOne(h => h.HelpRequest)
                .WithMany(r => r.StatusHistory)
                .HasForeignKey(h => h.HelpRequestId)
                .OnDelete(DeleteBehavior.Cascade);

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
        }
    }
}