using Microsoft.EntityFrameworkCore;
using RescueSriLanka.Api.Models;

namespace RescueSriLanka.Api.Data;

/// <summary>
/// Shared authentication context with Component A incident read mappings. Component D remains owned by
/// <see cref="ComponentDDbContext"/>.
/// </summary>
public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<Incident> Incidents => Set<Incident>();
    public DbSet<IncidentImage> IncidentImages => Set<IncidentImage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Existing Component A tables: this read integration must not manage their schema.
        modelBuilder.Entity<Incident>(entity =>
        {
            entity.ToTable("incidents", "public", table => table.ExcludeFromMigrations());
            entity.Property(incident => incident.Type).HasConversion<string>().HasMaxLength(40);
            entity.Property(incident => incident.Severity).HasConversion<string>().HasMaxLength(40);
            entity.Property(incident => incident.Status).HasConversion<string>().HasMaxLength(40);
            entity.Property(incident => incident.AiSeverity).HasConversion<string>().HasMaxLength(40);
            entity.HasMany(incident => incident.Images)
                .WithOne(image => image.Incident)
                .HasForeignKey(image => image.IncidentId)
                .OnDelete(DeleteBehavior.Cascade);
        });
        modelBuilder.Entity<IncidentImage>(entity =>
            entity.ToTable("incident_images", "public", table => table.ExcludeFromMigrations()));

        modelBuilder.Entity<User>(entity =>
        {
            entity.ToTable("users", "public");
            entity.HasIndex(user => user.Email).IsUnique();
            entity.Property(user => user.Role)
                .HasConversion<string>()
                .HasMaxLength(40);
        });
    }
}
