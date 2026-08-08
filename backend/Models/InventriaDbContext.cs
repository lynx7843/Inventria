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

        // Usernames identify an account to log in as, so two of them is an
        // authentication bug, not just untidy data. The Any() check in
        // UsersController is a check-then-act that two concurrent creates can both
        // pass; this index is what actually holds the line.
        //
        // The length is here because it has to be: string properties map to
        // nvarchar(max) by default and SQL Server cannot build an index over that.
        modelBuilder.Entity<User>(user =>
        {
            user.Property(u => u.Username).HasMaxLength(100);
            user.HasIndex(u => u.Username).IsUnique();
        });

        // A SKU is the code people scan and search by, so duplicates make the
        // wrong item pickable. Same nvarchar(max) constraint as above.
        modelBuilder.Entity<Item>(item =>
        {
            item.Property(i => i.Sku).HasMaxLength(64);
            item.HasIndex(i => i.Sku).IsUnique();
        });
    }
}