using ArcadeLink.Catalog.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace ArcadeLink.Catalog.Api.Data;

public class CatalogDbContext(DbContextOptions<CatalogDbContext> options) : DbContext(options)
{
    public DbSet<Game> Games => Set<Game>();
    public DbSet<UserLibraryEntry> LibraryEntries => Set<UserLibraryEntry>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Game>(entity =>
        {
            entity.ToTable("Games");
            entity.HasKey(g => g.Id);
            entity.Property(g => g.Title).IsRequired().HasMaxLength(200);
            entity.Property(g => g.Genre).HasMaxLength(100);
            entity.Property(g => g.Price).HasColumnType("numeric(10,2)");
        });

        modelBuilder.Entity<UserLibraryEntry>(entity =>
        {
            entity.ToTable("LibraryEntries");
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => new { e.UserId, e.GameId }).IsUnique();
        });
    }
}
