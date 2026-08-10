using Inventria.Models;
using Microsoft.EntityFrameworkCore;

namespace Inventria.Tests;

/// <summary>
/// The rules the database itself enforces, rather than the ones a controller
/// checks first. Every guard in this app is a check-then-act - count the
/// references, then delete - and two requests can interleave between those two
/// steps. These constraints are what actually holds the line when they do.
/// </summary>
public class SchemaConstraintTests
{
    [Fact]
    public void Stock_cannot_be_recorded_against_an_item_that_does_not_exist()
    {
        using var db = new TestDatabase();
        var bin = db.AddBin();

        db.Context.InventoryBalances.Add(new InventoryBalance
        {
            ItemId = 999,
            WarehouseBinId = bin.Id,
            Quantity = 5
        });

        Assert.Throws<DbUpdateException>(() => db.Context.SaveChanges());
    }

    [Fact]
    public void Stock_cannot_be_recorded_against_a_bin_that_does_not_exist()
    {
        using var db = new TestDatabase();
        var item = db.AddItem();

        db.Context.InventoryBalances.Add(new InventoryBalance
        {
            ItemId = item.Id,
            WarehouseBinId = 999,
            Quantity = 5
        });

        Assert.Throws<DbUpdateException>(() => db.Context.SaveChanges());
    }

    [Fact]
    public void A_movement_cannot_name_an_item_that_does_not_exist()
    {
        using var db = new TestDatabase();
        var bin = db.AddBin();

        db.Context.StockMovements.Add(new StockMovement
        {
            ItemId = 999,
            WarehouseBinId = bin.Id,
            TransactionType = "RECEIVE",
            QuantityChanged = 5,
            Timestamp = DateTime.UtcNow,
            PerformedBy = "alice"
        });

        // The audit log outlives an item's usefulness, so the relationship
        // exists to refuse a delete rather than follow it - and equally to
        // refuse a movement pointing at nothing.
        Assert.Throws<DbUpdateException>(() => db.Context.SaveChanges());
    }

    [Fact]
    public void Deleting_an_item_that_still_holds_stock_is_refused_by_the_database()
    {
        using var db = new TestDatabase();
        var item = db.AddItem();
        db.AddBalance(item, db.AddBin(), 5);

        // Through a context that has not loaded the balance rows, which is how
        // the controller works: it finds the item by id and removes it, so the
        // DELETE actually reaches the database. A context already tracking the
        // dependents never gets that far - EF sees the relationship severed and
        // objects first, which tests the change tracker rather than the schema.
        using var deleting = db.NewContext();
        deleting.Items.Remove(deleting.Items.Find(item.Id)!);

        var error = Assert.Throws<DbUpdateException>(() => deleting.SaveChanges());

        // Restrict, not cascade. Deleting a definition does not empty a shelf,
        // and the version of this that cascaded made deleting an item a way to
        // silently write off the stock recorded against it.
        Assert.NotNull(error.InnerException);
    }

    [Fact]
    public void Deleting_a_bin_that_still_holds_stock_is_refused_by_the_database()
    {
        using var db = new TestDatabase();
        var bin = db.AddBin();
        db.AddBalance(db.AddItem(), bin, 5);

        // A fresh context, for the same reason as the item above.
        using var deleting = db.NewContext();
        deleting.WarehouseBins.Remove(deleting.WarehouseBins.Find(bin.Id)!);

        Assert.Throws<DbUpdateException>(() => deleting.SaveChanges());
    }

    [Fact]
    public void One_item_cannot_have_two_balance_rows_in_the_same_bin()
    {
        using var db = new TestDatabase();
        var item = db.AddItem();
        var bin = db.AddBin();
        db.AddBalance(item, bin, 5);

        // Two concurrent receives both find no row and both insert; the unique
        // index is what makes the second one fail so it can be retried against
        // the row the first one created. Without it the stock splits across two
        // rows that later lookups choose between arbitrarily.
        db.Context.InventoryBalances.Add(new InventoryBalance
        {
            ItemId = item.Id,
            WarehouseBinId = bin.Id,
            Quantity = 7
        });

        Assert.Throws<DbUpdateException>(() => db.Context.SaveChanges());
    }

    [Fact]
    public void Two_items_cannot_share_a_sku()
    {
        using var db = new TestDatabase();
        db.AddItem("SKU-1", "Wrench");

        db.Context.Items.Add(new Item { Sku = "SKU-1", Name = "Different Wrench", Category = "Tools" });

        Assert.Throws<DbUpdateException>(() => db.Context.SaveChanges());
    }

    [Fact]
    public void Two_bins_cannot_share_an_address()
    {
        using var db = new TestDatabase();
        db.AddBin("Electronics", "A1", "S1");

        // Two rows with the same address are two ids for one physical shelf, and
        // stock received into one is invisible to anyone looking at the other.
        db.Context.WarehouseBins.Add(new WarehouseBin { Zone = "Electronics", Aisle = "A1", Shelf = "S1" });

        Assert.Throws<DbUpdateException>(() => db.Context.SaveChanges());
    }

    [Fact]
    public void Two_accounts_cannot_share_a_username()
    {
        using var db = new TestDatabase();
        db.Context.Users.Add(new User { Username = "admin", Password = "hash", Role = UserRoles.Admin });
        db.Context.SaveChanges();

        // Two of these is an authentication bug, not just untidy data.
        db.Context.Users.Add(new User { Username = "admin", Password = "other", Role = UserRoles.Employee });

        Assert.Throws<DbUpdateException>(() => db.Context.SaveChanges());
    }
}
