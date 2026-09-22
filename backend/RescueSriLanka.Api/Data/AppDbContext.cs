using Microsoft.EntityFrameworkCore;
using RescueSriLanka.Api.Models;

namespace RescueSriLanka.Api.Data;

/// <summary>
/// Authentication schema context. Component D remains owned by
/// <see cref="ComponentDDbContext"/>.
/// </summary>
public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

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
