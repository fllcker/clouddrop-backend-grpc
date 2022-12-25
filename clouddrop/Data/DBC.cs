using clouddrop.Models;
using Microsoft.EntityFrameworkCore;

namespace clouddrop.Data;

public class DBC : DbContext
{
    public DBC(DbContextOptions<DBC> options)
        : base(options)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Content>()
            .HasMany(c => c.Children)
            .WithOne(c => c.Parent)
            .HasForeignKey(c => c.ParentId);
    }

    public DbSet<User> Users { get; set; } = default!;
    public DbSet<Storage> Storages { get; set; } = default!;
    public DbSet<Content> Contents { get; set; } = default!;
}