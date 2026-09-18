using Microsoft.EntityFrameworkCore;
using RescueSriLanka.Api.Models;

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

        // Teammates: add your DbSets here too (Incident, HelpRequest,
        // Shelter, etc.) as your models land — keep this file shared.
    }
}
