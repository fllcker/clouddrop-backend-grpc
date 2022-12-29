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

        modelBuilder.Entity<Plan>()
            .HasData(new List<Plan>
            {
                new() {Id = 1, Name = "Basic", Description = "Default free plan", AvailableQuote = 52428800},
                new() {Id = 2, Name = "Premium", Description = "Middle premium plan", AvailableQuote = 524288000},
                new() {Id = 3, Name = "Supporter", Description = "Plan for real funs", AvailableQuote = 10485760000},
            });
    }

    public DbSet<User> Users { get; set; } = default!;
    public DbSet<Storage> Storages { get; set; } = default!;
    public DbSet<Content> Contents { get; set; } = default!;
    public DbSet<Plan> Plans { get; set; } = default!;
    public DbSet<Subscription> Subscriptions { get; set; } = default!;
    public DbSet<PurchaseCode> PurchaseCodes { get; set; } = default!;
}