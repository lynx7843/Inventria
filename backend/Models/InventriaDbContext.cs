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

        modelBuilder.Entity<InventoryBalance>(balance =>
        {
            // A RowVersion only guards a row that already exists. Two concurrent
            // receives for an item/bin pair with no balance row yet would both see
            // nothing and both insert, splitting the stock across two rows that later
            // lookups choose between arbitrarily. The unique index makes the second
            // insert fail so it can be retried against the row the first one created.
            balance.HasIndex(b => new { b.ItemId, b.WarehouseBinId }).IsUnique();

            // This relationship used to cascade, which made deleting an item a way
            // to silently destroy the stock recorded against it: the item row went
            // and every balance row went with it, on-hand quantity included. A
            // balance is a count of physical goods on a shelf and deleting a
            // definition does not empty the shelf, so the database now refuses the
            // delete instead of following it.
            balance.HasOne(b => b.Item)
                .WithMany()
                .HasForeignKey(b => b.ItemId)
                .OnDelete(DeleteBehavior.Restrict);

            // Same story for the bin the stock sits in: retiring a location is a
            // decision about the warehouse map, not permission to make the goods
            // on that shelf disappear from the books.
            balance.HasOne(b => b.WarehouseBin)
                .WithMany()
                .HasForeignKey(b => b.WarehouseBinId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // StockMovements had no foreign key at all, so a deleted item left its
        // movements behind pointing at an Id that resolves to nothing - the rows
        // the dashboard renders with a blank item name. The audit log is the
        // record of what happened and outlives the item's usefulness, so the
        // relationship exists to refuse the delete rather than to follow it. No
        // navigation property: a movement is written and read as a flat row, and
        // the only thing needed here is the constraint.
        modelBuilder.Entity<StockMovement>(movement =>
        {
            // Every movement is stamped with DateTime.UtcNow, but datetime2 has
            // no room for that fact: the value comes back with Kind=Unspecified,
            // serializes as "2026-08-10T09:42:00" with no trailing Z, and
            // JavaScript's Date() reads a string without a zone as local time.
            // The audit log then shifted by the viewer's UTC offset - a receive
            // logged at 09:42 UTC read as 09:42 in Manila, seven hours early, and
            // the error was invisible to anyone sitting in UTC.
            //
            // Stamping the Kind here rather than at each endpoint means the value
            // is right everywhere it is read, including wherever the log is
            // surfaced next. The write side only has to correct a Local time;
            // Unspecified is assumed to already be UTC, because everything that
            // writes this column writes UtcNow.
            movement.Property(m => m.Timestamp)
                .HasConversion(
                    write => write.Kind == DateTimeKind.Local ? write.ToUniversalTime() : write,
                    read => DateTime.SpecifyKind(read, DateTimeKind.Utc));

            movement.HasOne<Item>()
                .WithMany()
                .HasForeignKey(m => m.ItemId)
                .OnDelete(DeleteBehavior.Restrict);

            // The bin is half of what a movement says - "20 units left A-1-1" is
            // the whole record - so a deleted bin would leave the log saying
            // units left somewhere unnamed. Nullable, because the column is:
            // the constraint only applies to rows that name a bin.
            movement.HasOne<WarehouseBin>()
                .WithMany()
                .HasForeignKey(m => m.WarehouseBinId)
                .OnDelete(DeleteBehavior.Restrict);
        });

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

        // Zone/Aisle/Shelf together are the address a picker walks to, so two
        // rows with the same three values are two Ids for one physical shelf -
        // stock received into one of them is invisible to anyone looking at the
        // other. Same nvarchar(max) constraint as above; the lengths are what a
        // location code plausibly needs rather than a guess at a storage limit.
        modelBuilder.Entity<WarehouseBin>(bin =>
        {
            bin.Property(b => b.Zone).HasMaxLength(64);
            bin.Property(b => b.Aisle).HasMaxLength(32);
            bin.Property(b => b.Shelf).HasMaxLength(32);
            bin.HasIndex(b => new { b.Zone, b.Aisle, b.Shelf }).IsUnique();
        });
    }
}