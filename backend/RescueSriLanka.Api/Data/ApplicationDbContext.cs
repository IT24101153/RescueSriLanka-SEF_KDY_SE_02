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

            // NOTE for teammates: add your own entity configuration below
            // this line rather than replacing the method — keep this file
            // additive since it's shared across all four components.
        }
    }
}
