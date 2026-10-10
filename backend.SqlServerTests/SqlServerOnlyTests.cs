using Inventria.Models;
using Microsoft.EntityFrameworkCore;

namespace Inventria.SqlServerTests;

/// <summary>
/// The two things the SQLite suite cannot cover (see TestDatabase.cs):
/// RowVersion optimistic concurrency, and UniqueConstraint.WasViolated matching
/// the error numbers SQL Server really raises.
/// </summary>
public class SqlServerOnlyTests
{
    [SqlServerFact]
    public void A_duplicate_sku_is_recognised_as_a_unique_violation()
    {
        using var db = new SqlServerDatabase();
        using var context = db.NewContext();
        context.Items.Add(new Item { Sku = "DUP", Name = "First", Category = "Tools" });
        context.SaveChanges();

        context.Items.Add(new Item { Sku = "DUP", Name = "Second", Category = "Tools" });
        var ex = Assert.Throws<DbUpdateException>(() => context.SaveChanges());

        Assert.True(UniqueConstraint.WasViolated(ex));
    }

    [SqlServerFact]
    public void Writing_a_balance_changed_since_it_was_read_is_refused()
    {
        using var db = new SqlServerDatabase();
        var (itemId, binId) = db.AddItemAndBin();
        using (var seed = db.NewContext())
        {
            seed.InventoryBalances.Add(new InventoryBalance { ItemId = itemId, WarehouseBinId = binId, Quantity = 10 });
            seed.SaveChanges();
        }

        using var first = db.NewContext();
        using var second = db.NewContext();
        var firstCopy = first.InventoryBalances.Single();
        var secondCopy = second.InventoryBalances.Single();

        firstCopy.Quantity += 5;
        first.SaveChanges();

        secondCopy.Quantity += 7;
        Assert.Throws<DbUpdateConcurrencyException>(() => second.SaveChanges());

        using var check = db.NewContext();
        Assert.Equal(15, check.InventoryBalances.Single().Quantity);
    }

    [SqlServerFact]
    public async Task Simultaneous_receives_into_one_bin_all_land()
    {
        using var db = new SqlServerDatabase();
        var (itemId, binId) = db.AddItemAndBin();

        // Each receive gets its own context, as each request would.
        await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => Task.Run(() =>
        {
            using var context = db.NewContext();
            var item = context.Items.Single(i => i.Id == itemId);
            var receipt = new StockReceivingService(context).Receive(item, binId, 1, "alice", null, null);
            Assert.NotNull(receipt);
        })));

        using var check = db.NewContext();
        Assert.Equal(6, check.InventoryBalances.Single().Quantity);
        Assert.Equal(6, check.StockMovements.Count());
    }
}
