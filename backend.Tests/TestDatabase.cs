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

    public Item AddItem(string sku = "SKU-1", string name = "Steel Wrench", string category = "Tools")
    {
        var item = new Item { Sku = sku, Name = name, Category = category };
        Context.Items.Add(item);
        Context.SaveChanges();
        return item;
    }

    public WarehouseBin AddBin(string zone = "Electronics", string aisle = "A1", string shelf = "S1")
    {
        var bin = new WarehouseBin { Zone = zone, Aisle = aisle, Shelf = shelf };
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
        }
    }
}
