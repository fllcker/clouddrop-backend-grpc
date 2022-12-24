using clouddrop.Models;
using Microsoft.EntityFrameworkCore;

namespace clouddrop.Data;

public class DBC : DbContext
{
    public DBC(DbContextOptions<DBC> options)
        : base(options)
    {
    }

    public DbSet<User> Users { get; set; } = default!;
}