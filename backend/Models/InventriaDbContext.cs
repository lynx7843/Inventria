using Microsoft.EntityFrameworkCore;

namespace Inventria.Models;

public class InventriaDbContext : DbContext
{
    public InventriaDbContext(DbContextOptions<InventriaDbContext> options) : base(options) { }

    // This property tells Entity Framework to create a table named "Users"
    public DbSet<User> Users { get; set; }
}