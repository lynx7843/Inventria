using Inventria.Models;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Inventria.Tests;

/// <summary>
/// A real relational database for one test, held in memory.
///
/// SQLite rather than EF's in-memory provider because most of what is worth
/// testing here is relational behaviour - a foreign key that refuses a delete, a
/// unique index, a query that has to translate to SQL. The in-memory provider
/// enforces none of that and would pass tests that the real database fails.
///
/// The connection is kept open for the lifetime of the fixture: an in-memory
/// SQLite database exists only as long as a connection to it does, so closing it
/// early would take the schema with it.
///
/// Two things do not carry over from SQL Server, and no test here pretends
/// otherwise. RowVersion is a SQL Server type, so the optimistic concurrency it
/// backs cannot be exercised (see the note on the default below). And a
/// duplicate-key failure arrives as a SqliteException, which the controllers'
/// `UniqueConstraint.WasViolated` filters do not recognise - so the paths that
/// turn a race into a friendly 400 are left to the SQL Server test pass.
/// </summary>
public sealed class TestDatabase : IDisposable
{
    private readonly SqliteConnection _connection;

    public InventriaDbContext Context { get; }

    public TestDatabase()
    {
        _connection = new SqliteConnection("Filename=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<InventriaDbContext>()
            .UseSqlite(_connection)
            .Options;

        Context = new SqliteInventriaDbContext(options);
        Context.Database.EnsureCreated();
    }

    /// <summary>
    /// A second context over the same database, for asserting on what was
    /// actually written rather than on what the change tracker remembers.
    /// </summary>
    public InventriaDbContext NewContext()
    {
        var options = new DbContextOptionsBuilder<InventriaDbContext>()
            .UseSqlite(_connection)
            .Options;

        return new SqliteInventriaDbContext(options);
    }

    /// <summary>
    /// An item in the master list. Reorder levels default to zero - "not tracked"
    /// - so every test that is not about reordering gets an item the low-stock
    /// machinery ignores, which is also what the real catalogue looked like
    /// before those columns existed.
    /// </summary>
    public Item AddItem(
        string sku = "SKU-1",
        string name = "Steel Wrench",
        string category = "Tools",
        int reorderPoint = 0,
        int reorderQuantity = 0,
        bool isArchived = false,
        bool tracksLots = false)
    {
        var item = new Item
        {
            Sku = sku,
            Name = name,
            Category = category,
            ReorderPoint = reorderPoint,
            ReorderQuantity = reorderQuantity,
            IsArchived = isArchived,
            TracksLots = tracksLots
        };
        Context.Items.Add(item);
        Context.SaveChanges();
        return item;
    }

    /// <summary>
    /// An account, for the endpoints that read something off the caller's own
    /// row rather than off their token - the low-stock warning a pick answers
    /// with is gated on this user's NotifyLowStock preference.
    /// </summary>
    public User AddUser(int id = 1, string username = "alice", bool notifyLowStock = true)
    {
        var user = new User
        {
            Id = id,
            Username = username,
            Password = "hash",
            Role = UserRoles.Employee,
            NotifyLowStock = notifyLowStock
        };
        Context.Users.Add(user);
        Context.SaveChanges();
        return user;
    }

    /// <summary>Who a purchase order is placed with.</summary>
    public Supplier AddSupplier(string name = "Acme Supply Co")
    {
        var supplier = new Supplier { Name = name };
        Context.Suppliers.Add(supplier);
        Context.SaveChanges();
        return supplier;
    }

    private int? _defaultWarehouseId;

    /// <summary>
    /// A building for bins to belong to. Lazily created and reused so every
    /// existing call to AddBin - which predates warehouses entirely - keeps
    /// landing every bin in the one building it always implicitly meant,
    /// without every one of those call sites having to know that.
    /// </summary>
    public Warehouse AddWarehouse(string name = "Main Warehouse")
    {
        var warehouse = new Warehouse { Name = name };
        Context.Warehouses.Add(warehouse);
        Context.SaveChanges();
        return warehouse;
    }

    public WarehouseBin AddBin(string zone = "Electronics", string aisle = "A1", string shelf = "S1", int? warehouseId = null)
    {
        var resolvedWarehouseId = warehouseId ?? (_defaultWarehouseId ??= AddWarehouse().Id);
        var bin = new WarehouseBin { WarehouseId = resolvedWarehouseId, Zone = zone, Aisle = aisle, Shelf = shelf };
        Context.WarehouseBins.Add(bin);
        Context.SaveChanges();
        return bin;
    }

    /// <summary>Puts stock on a shelf without going through the API.</summary>
    public InventoryBalance AddBalance(Item item, WarehouseBin bin, int quantity)
    {
        var balance = new InventoryBalance { ItemId = item.Id, WarehouseBinId = bin.Id, Quantity = quantity };
        Context.InventoryBalances.Add(balance);
        Context.SaveChanges();
        return balance;
    }

    /// <summary>A named batch of a lot-tracked item, with an optional expiration date.</summary>
    public Lot AddLot(Item item, string lotNumber = "LOT-1", DateTime? expirationDate = null)
    {
        var lot = new Lot { ItemId = item.Id, LotNumber = lotNumber, ExpirationDate = expirationDate };
        Context.Lots.Add(lot);
        Context.SaveChanges();
        return lot;
    }

    /// <summary>Puts a lot's stock on a shelf without going through the API.</summary>
    public InventoryLotBalance AddLotBalance(Item item, WarehouseBin bin, Lot lot, int quantity)
    {
        var balance = new InventoryLotBalance { ItemId = item.Id, WarehouseBinId = bin.Id, LotId = lot.Id, Quantity = quantity };
        Context.InventoryLotBalances.Add(balance);
        Context.SaveChanges();
        return balance;
    }

    /// <summary>
    /// Writes a movement against an item id that does not exist.
    ///
    /// The foreign key refuses this, which is the point of it - so the only way
    /// such a row exists is the way it does in the real database: written before
    /// the constraint was added, and left in place by a migration that installed
    /// it WITH NOCHECK rather than delete audit history to satisfy it. Turning
    /// the enforcement off for one insert is how that history gets reproduced
    /// here. Raw SQL because a PRAGMA is ignored inside the transaction
    /// SaveChanges would open.
    /// </summary>
    public void AddLegacyOrphanedMovement(int missingItemId, int binId, int quantity)
    {
        Context.Database.ExecuteSqlRaw("PRAGMA foreign_keys = OFF;");

        Context.Database.ExecuteSqlRaw(
            """
            INSERT INTO StockMovements (ItemId, WarehouseBinId, TransactionType, QuantityChanged, Timestamp, PerformedBy)
            VALUES ({0}, {1}, 'RECEIVE', {2}, {3}, 'alice');
            """,
            missingItemId, binId, quantity, DateTime.UtcNow);

        Context.Database.ExecuteSqlRaw("PRAGMA foreign_keys = ON;");
    }

    public void Dispose()
    {
        Context.Dispose();
        _connection.Dispose();
    }

    private sealed class SqliteInventriaDbContext(DbContextOptions<InventriaDbContext> options)
        : InventriaDbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // SQLite has no rowversion column type, so nothing fills this in and
            // the insert fails on a NOT NULL it cannot satisfy. A random blob
            // gets rows written; it does not change on update, so the token
            // never trips and concurrency is simply not under test here.
            modelBuilder.Entity<InventoryBalance>()
                .Property(balance => balance.RowVersion)
                .HasDefaultValueSql("randomblob(8)");

            modelBuilder.Entity<InventoryLotBalance>()
                .Property(balance => balance.RowVersion)
                .HasDefaultValueSql("randomblob(8)");
        }
    }
}
