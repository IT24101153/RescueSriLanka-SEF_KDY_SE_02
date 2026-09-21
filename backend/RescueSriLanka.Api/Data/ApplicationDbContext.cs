using Microsoft.EntityFrameworkCore;
using RescueSriLanka.Api.Models;
using RescueSriLanka.Api.Models.Agents;

namespace RescueSriLanka.Api.Data
{
    public class ApplicationDbContext : DbContext
    {
        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
            : base(options) { }

        // Component D
        public DbSet<RescueTeam> RescueTeams => Set<RescueTeam>();
        public DbSet<TeamMember> TeamMembers => Set<TeamMember>();
        public DbSet<Vehicle> Vehicles => Set<Vehicle>();
        public DbSet<Assignment> Assignments => Set<Assignment>();
        public DbSet<Dispatch> Dispatches => Set<Dispatch>();

        // Shared Agentic AI workflow state (see Models/Agents/AgentWorkflow.cs)
        public DbSet<AgentWorkflow> AgentWorkflows => Set<AgentWorkflow>();
        public DbSet<AgentStep> AgentSteps => Set<AgentStep>();

        // Teammates: add your DbSets here too (Incident, HelpRequest,
        // Shelter, etc.) as your models land — keep this file shared.

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // Enforces the 1:1 invariant between Assignment and Dispatch
            // even under concurrent requests (see the validation/bugfix
            // refinement pass for the full explanation).
            modelBuilder.Entity<Dispatch>()
                .HasIndex(d => d.AssignmentId)
                .IsUnique();

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

            modelBuilder.Entity<AgentStep>()
                .HasOne(s => s.Workflow)
                .WithMany(w => w.Steps)
                .HasForeignKey(s => s.AgentWorkflowId)
                .OnDelete(DeleteBehavior.Cascade);

            // NOTE for teammates: add your own entity configuration below
            // this line rather than replacing the method — keep this file
            // additive since it's shared across all four components.
        }
    }
}
