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
    }
}
