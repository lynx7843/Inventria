using Microsoft.EntityFrameworkCore;

namespace Inventria.Models;

public class InventriaDbContext : DbContext
{
    public InventriaDbContext(DbContextOptions<InventriaDbContext> options) : base(options) { }

    public DbSet<User> Users { get; set; }
    
    // Add these new inventory tables
    public DbSet<Item> Items { get; set; }
    public DbSet<WarehouseBin> WarehouseBins { get; set; }
    public DbSet<InventoryBalance> InventoryBalances { get; set; }
    public DbSet<StockMovement> StockMovements { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // A RowVersion only guards a row that already exists. Two concurrent
        // receives for an item/bin pair with no balance row yet would both see
        // nothing and both insert, splitting the stock across two rows that later
        // lookups choose between arbitrarily. The unique index makes the second
        // insert fail so it can be retried against the row the first one created.
        modelBuilder.Entity<InventoryBalance>()
            .HasIndex(b => new { b.ItemId, b.WarehouseBinId })
            .IsUnique();
    }
}