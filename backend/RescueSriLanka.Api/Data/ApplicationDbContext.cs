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
        }
    }
}