using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using System.Security.Claims;
using System.Text.Json;

namespace Inventria.Models;

public class InventriaDbContext : DbContext
{
    // Nullable and defaulted rather than required: tests construct this
    // context directly with just options (see TestDatabase), and the audit
    // trail below already falls back to "system" with no accessor at all.
    private readonly IHttpContextAccessor? _httpContextAccessor;

    public InventriaDbContext(DbContextOptions<InventriaDbContext> options) : this(options, null) { }

    public InventriaDbContext(DbContextOptions<InventriaDbContext> options, IHttpContextAccessor? httpContextAccessor)
        : base(options)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public DbSet<User> Users { get; set; }
    
    // Add these new inventory tables
    public DbSet<Item> Items { get; set; }
    public DbSet<WarehouseBin> WarehouseBins { get; set; }
    public DbSet<InventoryBalance> InventoryBalances { get; set; }
    public DbSet<StockMovement> StockMovements { get; set; }
    public DbSet<Supplier> Suppliers { get; set; }
    public DbSet<Lot> Lots { get; set; }
    public DbSet<InventoryLotBalance> InventoryLotBalances { get; set; }
    public DbSet<Warehouse> Warehouses { get; set; }
    public DbSet<PurchaseOrder> PurchaseOrders { get; set; }
    public DbSet<PurchaseOrderLine> PurchaseOrderLines { get; set; }
    public DbSet<CountSheet> CountSheets { get; set; }
    public DbSet<CountSheetLine> CountSheetLines { get; set; }
    public DbSet<PickList> PickLists { get; set; }
    public DbSet<PickListLine> PickListLines { get; set; }
    public DbSet<Transfer> Transfers { get; set; }
    public DbSet<TransferLine> TransferLines { get; set; }
    public DbSet<BillOfMaterials> BillsOfMaterials { get; set; }
    public DbSet<BillOfMaterialLine> BillOfMaterialLines { get; set; }
    public DbSet<AuditLog> AuditLogs { get; set; }
    public DbSet<RecoveryCode> RecoveryCodes { get; set; }

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
            movement.Property(m => m.Timestamp).HasConversion(v => ToUtcForWrite(v), v => AsUtcOnRead(v));

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

            // Same reasoning as the bin above: a movement that named a lot
            // said something happened to that batch, and a deleted lot would
            // leave the audit log pointing at a recall that no longer
            // resolves to anything. Nullable for the same reason the bin is -
            // only movements against a lot-tracked item ever set it.
            movement.HasOne(m => m.Lot)
                .WithMany()
                .HasForeignKey(m => m.LotId)
                .OnDelete(DeleteBehavior.Restrict);

            // Free text, but not unbounded free text - the same reasoning as
            // Item.Category: past this length it has stopped being a reason
            // code and started being pasted junk. 500 rather than 200: a
            // return's reason is customer-facing prose, not a short count
            // sheet note, so it needs more room.
            movement.Property(m => m.ReasonCode).HasMaxLength(500);

            movement.Property(m => m.ConditionGrade).HasMaxLength(32);
            movement.Property(m => m.Disposition).HasMaxLength(32);
        });

        // A lot is scoped to one item - "LOT-2026-01" from one supplier means
        // nothing to another item that happens to reuse the string - so the
        // uniqueness that matters is the pair, not the number alone.
        modelBuilder.Entity<Lot>(lot =>
        {
            lot.Property(l => l.LotNumber).HasMaxLength(64);
            lot.HasIndex(l => new { l.ItemId, l.LotNumber }).IsUnique();

            // Same story as InventoryBalance's Item relationship: a lot is a
            // record of a batch that existed, and deleting the item it
            // belonged to does not un-receive that batch.
            lot.HasOne(l => l.Item)
                .WithMany()
                .HasForeignKey(l => l.ItemId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<InventoryLotBalance>(balance =>
        {
            // The three-way equivalent of InventoryBalance's unique index,
            // for the same reason: two concurrent first-receipts of one
            // lot into one bin must not be able to both insert.
            balance.HasIndex(b => new { b.ItemId, b.WarehouseBinId, b.LotId }).IsUnique();

            balance.HasOne(b => b.Item)
                .WithMany()
                .HasForeignKey(b => b.ItemId)
                .OnDelete(DeleteBehavior.Restrict);

            balance.HasOne(b => b.WarehouseBin)
                .WithMany()
                .HasForeignKey(b => b.WarehouseBinId)
                .OnDelete(DeleteBehavior.Restrict);

            balance.HasOne(b => b.Lot)
                .WithMany()
                .HasForeignKey(b => b.LotId)
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

            // A bare C# property initializer only sets what a newly constructed
            // User looks like in memory - EF Core infers a column default from
            // the CLR default of the type (false) unless told otherwise, which
            // would give every row added by this migration NotifyLowStock = false
            // regardless of what the property declares. Spelled out here so the
            // column - and the next `has-pending-model-changes` check - agree
            // with it.
            user.Property(u => u.NotifyLowStock).HasDefaultValue(true);
            user.Property(u => u.NotifyDailySummary).HasDefaultValue(false);

            // Long enough for "uploads/avatars/" plus a GUID32 filename and
            // extension with room to spare - not a guess at a storage limit,
            // since AvatarStorage is the only thing that ever writes this column.
            user.Property(u => u.AvatarPath).HasMaxLength(255);

            // A version byte, a 12-byte nonce, a 16-byte tag and 20 bytes of
            // secret, base64'd - 68 characters. The cap is generous rather than
            // exact so a future format (see TotpSecretProtector.Version) has
            // room without a migration, but it is a cap: these are never
            // indexed, but nvarchar(max) for a 68-character column is a row
            // that is stored off-page for no reason.
            user.Property(u => u.TotpSecret).HasMaxLength(256);
            user.Property(u => u.TotpPendingSecret).HasMaxLength(256);

            user.Property(u => u.TotpEnabledAt)
                .HasConversion(v => ToUtcForWriteNullable(v), v => AsUtcOnReadNullable(v));
        });

        // Single-use codes for an account whose authenticator app is gone.
        modelBuilder.Entity<RecoveryCode>(code =>
        {
            // Hex SHA-256 is exactly 64 characters.
            code.Property(c => c.CodeHash).HasMaxLength(64);

            code.Property(c => c.CreatedAt).HasConversion(v => ToUtcForWrite(v), v => AsUtcOnRead(v));
            code.Property(c => c.UsedAt)
                .HasConversion(v => ToUtcForWriteNullable(v), v => AsUtcOnReadNullable(v));

            // Every read of this table is "the codes belonging to this account",
            // during a sign-in that is already waiting on the person typing.
            code.HasIndex(c => c.UserId);

            // Deleting an account takes its recovery codes with it. Cascade
            // rather than the restrict used for stock, because unlike a
            // movement these are worth nothing once the account they open is
            // gone - and leaving them would block the delete outright.
            code.HasOne(c => c.User)
                .WithMany(u => u.RecoveryCodes)
                .HasForeignKey(c => c.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // A SKU is the code people scan and search by, so duplicates make the
        // wrong item pickable. Same nvarchar(max) constraint as above.
        modelBuilder.Entity<Item>(item =>
        {
            item.Property(i => i.Sku).HasMaxLength(64);
            item.HasIndex(i => i.Sku).IsUnique();
            item.Property(i => i.UnitOfMeasure).HasMaxLength(32).HasDefaultValue("unit");

            // decimal, never double: binary floating point cannot represent
            // 0.10 exactly, and an error that small compounds over a
            // warehouse's worth of quantities. Precision is spelled out here
            // rather than left to EF's default (which triggers a warning and
            // leaves SQL Server to pick one on its own) - 18,2 is 16 digits
            // ahead of the decimal point, more than any unit cost needs, and
            // 2 behind it, which is all money needs.
            item.Property(i => i.UnitCost).HasPrecision(18, 2);
            item.Property(i => i.SalePrice).HasPrecision(18, 2);

            // An ordinary unique index treats every NULL as equal to every
            // other NULL, so the moment a second item went unbarcoded the
            // index would refuse to let it save. Filtering the index to rows
            // that actually have a barcode is what makes "not barcoded yet"
            // possible for more than one item at a time, while still refusing
            // two items claiming the same code.
            item.Property(i => i.Barcode).HasMaxLength(64);
            item.HasIndex(i => i.Barcode).IsUnique().HasFilter("[Barcode] IS NOT NULL");

            // Same reasoning as Item's other two foreign keys above: a
            // supplier going out of the picture is a fact about the supplier,
            // not permission to erase which items used to be sourced from
            // them. Restrict means retiring a supplier requires clearing it
            // off items first, which is the point - it forces a conscious
            // reassignment rather than a silent one.
            item.HasOne(i => i.Supplier)
                .WithMany()
                .HasForeignKey(i => i.SupplierId)
                .OnDelete(DeleteBehavior.Restrict);

            // Same reasoning as NotifyLowStock on User: a bare property
            // initializer only sets what a newly constructed Item looks like
            // in memory, not the column default EF infers, so it is spelled
            // out here to keep the model and the `has-pending-model-changes`
            // check agreeing with it.
            item.Property(i => i.IsArchived).HasDefaultValue(false);

            // Same reasoning again: every item that existed before this
            // column did was tracked the only way the system knew how, which
            // is what false means here.
            item.Property(i => i.TracksLots).HasDefaultValue(false);
        });

        // Contact fields are free text with no uniqueness or length concern
        // beyond the ordinary nvarchar(max) default, so Supplier needs no
        // configuration block of its own beyond the relationship declared on
        // Item above.

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

            // Scoped to the warehouse now, not global: two different
            // buildings can each have an A1/S1, and that used to be
            // impossible to express because there was only ever one
            // building. See the AddWarehouse migration for how every bin
            // that predates WarehouseId keeps its address unique by landing
            // in the same single default warehouse.
            bin.HasIndex(b => new { b.WarehouseId, b.Zone, b.Aisle, b.Shelf }).IsUnique();

            // Same reasoning as Item's Supplier relationship: retiring a
            // warehouse is a fact about the warehouse, not permission to
            // erase the bins that were addresses within it.
            bin.HasOne(b => b.Warehouse)
                .WithMany()
                .HasForeignKey(b => b.WarehouseId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<PurchaseOrder>(order =>
        {
            order.Property(o => o.Status).HasMaxLength(32);
            order.Property(o => o.CreatedBy).HasMaxLength(100);
            order.Property(o => o.Notes).HasMaxLength(1000);

            // Same Kind=Unspecified problem StockMovement.Timestamp already
            // documents, and the same fix - CreatedAt and OrderedAt are always
            // DateTime.UtcNow, so Unspecified read back from datetime2 is
            // assumed to already be UTC. ExpectedDate isn't always UtcNow - it's
            // whatever a caller sends - but it is still a UTC instant once
            // stored, so a value read back as Local rather than Unspecified
            // (possible from a client that never converts) is corrected the
            // same way on the way in.
            order.Property(o => o.CreatedAt).HasConversion(v => ToUtcForWrite(v), v => AsUtcOnRead(v));
            order.Property(o => o.OrderedAt).HasConversion(v => ToUtcForWriteNullable(v), v => AsUtcOnReadNullable(v));
            order.Property(o => o.ReceivedAt).HasConversion(v => ToUtcForWriteNullable(v), v => AsUtcOnReadNullable(v));
            order.Property(o => o.ExpectedDate).HasConversion(v => ToUtcForWriteNullable(v), v => AsUtcOnReadNullable(v));

            // A supplier retiring does not un-order what was already placed
            // with them - same reasoning as Item.Supplier and WarehouseBin.
            // Warehouse.
            order.HasOne(o => o.Supplier)
                .WithMany()
                .HasForeignKey(o => o.SupplierId)
                .OnDelete(DeleteBehavior.Restrict);

            order.HasMany(o => o.Lines)
                .WithOne(l => l.PurchaseOrder)
                .HasForeignKey(l => l.PurchaseOrderId)
                // Unlike Item/WarehouseBin/Supplier, a line has no meaning
                // apart from the order it belongs to - it is not a separate
                // record of anything. PurchaseOrdersController replaces a
                // Draft's lines by removing the tracked ones and adding new
                // ones, which EF turns into DELETEs regardless of this
                // setting; Cascade only matters if an order itself is ever
                // deleted, so that a Draft nobody placed can go without
                // orphaning the lines under it first.
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<PurchaseOrderLine>(line =>
        {
            line.Property(l => l.UnitCost).HasPrecision(18, 2);

            // Same reasoning as InventoryBalance.Item: an item's ordering
            // history outlives a decision to stop stocking it, so this
            // refuses the delete rather than following it.
            line.HasOne(l => l.Item)
                .WithMany()
                .HasForeignKey(l => l.ItemId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<CountSheet>(sheet =>
        {
            sheet.Property(s => s.Zone).HasMaxLength(64);
            sheet.Property(s => s.Status).HasMaxLength(32);
            sheet.Property(s => s.OpenedBy).HasMaxLength(100);
            sheet.Property(s => s.PostedBy).HasMaxLength(100);

            sheet.Property(s => s.OpenedAt).HasConversion(v => ToUtcForWrite(v), v => AsUtcOnRead(v));
            sheet.Property(s => s.PostedAt).HasConversion(v => ToUtcForWriteNullable(v), v => AsUtcOnReadNullable(v));

            // Same reasoning as every other Warehouse relationship: retiring a
            // building is not permission to erase the counts that were once
            // taken inside it.
            sheet.HasOne(s => s.Warehouse)
                .WithMany()
                .HasForeignKey(s => s.WarehouseId)
                .OnDelete(DeleteBehavior.Restrict);

            sheet.HasMany(s => s.Lines)
                .WithOne(l => l.CountSheet)
                .HasForeignKey(l => l.CountSheetId)
                // A line has no meaning apart from the sheet it belongs to -
                // same reasoning as PurchaseOrder.Lines above.
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<CountSheetLine>(line =>
        {
            line.Property(l => l.ReasonCode).HasMaxLength(200);

            // Same reasoning as InventoryBalance/InventoryLotBalance's
            // relationships: an item, bin or lot outliving a count that once
            // touched it is fine, but deleting one out from under a count sheet
            // that still refers to it would leave the audit trail pointing at
            // nothing.
            line.HasOne(l => l.Item)
                .WithMany()
                .HasForeignKey(l => l.ItemId)
                .OnDelete(DeleteBehavior.Restrict);

            line.HasOne(l => l.WarehouseBin)
                .WithMany()
                .HasForeignKey(l => l.WarehouseBinId)
                .OnDelete(DeleteBehavior.Restrict);

            line.HasOne(l => l.Lot)
                .WithMany()
                .HasForeignKey(l => l.LotId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<PickList>(list =>
        {
            list.Property(l => l.Status).HasMaxLength(32);
            list.Property(l => l.CreatedBy).HasMaxLength(100);

            list.Property(l => l.CreatedAt).HasConversion(v => ToUtcForWrite(v), v => AsUtcOnRead(v));
            list.Property(l => l.CompletedAt).HasConversion(v => ToUtcForWriteNullable(v), v => AsUtcOnReadNullable(v));

            list.HasMany(l => l.Lines)
                .WithOne(l => l.PickList)
                .HasForeignKey(l => l.PickListId)
                // A line has no meaning apart from the list it belongs to -
                // same reasoning as PurchaseOrder.Lines above.
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<PickListLine>(line =>
        {
            line.Property(l => l.LotNumber).HasMaxLength(64);

            // Same reasoning as CountSheetLine's relationships: an item or bin
            // outliving a pick list that once referred to it is fine; deleting
            // one out from under a list still referring to it is not.
            line.HasOne(l => l.Item)
                .WithMany()
                .HasForeignKey(l => l.ItemId)
                .OnDelete(DeleteBehavior.Restrict);

            line.HasOne(l => l.WarehouseBin)
                .WithMany()
                .HasForeignKey(l => l.WarehouseBinId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Transfer>(transfer =>
        {
            transfer.Property(t => t.Status).HasMaxLength(32);
            transfer.Property(t => t.CreatedBy).HasMaxLength(100);
            transfer.Property(t => t.ShippedBy).HasMaxLength(100);
            transfer.Property(t => t.ReceivedBy).HasMaxLength(100);

            transfer.Property(t => t.CreatedAt).HasConversion(v => ToUtcForWrite(v), v => AsUtcOnRead(v));
            transfer.Property(t => t.ShippedAt).HasConversion(v => ToUtcForWriteNullable(v), v => AsUtcOnReadNullable(v));
            transfer.Property(t => t.ReceivedAt).HasConversion(v => ToUtcForWriteNullable(v), v => AsUtcOnReadNullable(v));

            // Same reasoning as every other Warehouse relationship: retiring a
            // building is not permission to erase the transfers that once
            // moved stock into or out of it.
            transfer.HasOne(t => t.SourceWarehouse)
                .WithMany()
                .HasForeignKey(t => t.SourceWarehouseId)
                .OnDelete(DeleteBehavior.Restrict);

            transfer.HasOne(t => t.DestinationWarehouse)
                .WithMany()
                .HasForeignKey(t => t.DestinationWarehouseId)
                .OnDelete(DeleteBehavior.Restrict);

            transfer.HasMany(t => t.Lines)
                .WithOne(l => l.Transfer)
                .HasForeignKey(l => l.TransferId)
                // A line has no meaning apart from the transfer it belongs to -
                // same reasoning as PurchaseOrder.Lines above.
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<TransferLine>(line =>
        {
            line.Property(l => l.LotNumber).HasMaxLength(64);

            line.HasOne(l => l.Item)
                .WithMany()
                .HasForeignKey(l => l.ItemId)
                .OnDelete(DeleteBehavior.Restrict);

            // Two relationships to the same WarehouseBins table - EF needs
            // each one told apart by its own foreign key, since a line's
            // source and destination are never implied by the other.
            line.HasOne(l => l.SourceBin)
                .WithMany()
                .HasForeignKey(l => l.SourceBinId)
                .OnDelete(DeleteBehavior.Restrict);

            line.HasOne(l => l.DestinationBin)
                .WithMany()
                .HasForeignKey(l => l.DestinationBinId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<BillOfMaterials>(bom =>
        {
            // One bill of materials per item - a second one for the same item
            // would just be a second, competing answer to "what does this
            // build from".
            bom.HasIndex(b => b.ItemId).IsUnique();

            bom.HasOne(b => b.Item)
                .WithMany()
                .HasForeignKey(b => b.ItemId)
                .OnDelete(DeleteBehavior.Restrict);

            bom.HasMany(b => b.Components)
                .WithOne(c => c.BillOfMaterials)
                .HasForeignKey(c => c.BillOfMaterialsId)
                // A component line has no meaning apart from the bill of
                // materials it belongs to - same reasoning as PurchaseOrder.Lines.
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<BillOfMaterialLine>(line =>
        {
            // The same component listed twice on one bill of materials is
            // either a mistake or two different quantities for the same
            // answer to "how much of this do we need" - either way, not
            // something to store as two rows.
            line.HasIndex(l => new { l.BillOfMaterialsId, l.ComponentItemId }).IsUnique();

            line.HasOne(l => l.ComponentItem)
                .WithMany()
                .HasForeignKey(l => l.ComponentItemId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<AuditLog>(log =>
        {
            log.Property(l => l.Username).HasMaxLength(100);
            log.Property(l => l.Entity).HasMaxLength(100);
            log.Property(l => l.EntityId).HasMaxLength(100);
            log.Property(l => l.Action).HasMaxLength(20);

            log.Property(l => l.Timestamp).HasConversion(v => ToUtcForWrite(v), v => AsUtcOnRead(v));

            // The audit page (AuditLogsController) always reads newest first
            // and is the only thing that ever queries this table.
            log.HasIndex(l => l.Timestamp);
        });
    }

    // Shared by every DateTime column above that is written as UtcNow and has
    // to come back with Kind=Utc rather than Unspecified - see
    // StockMovement.Timestamp for why datetime2 loses that fact on its own.
    private static DateTime ToUtcForWrite(DateTime value) =>
        value.Kind == DateTimeKind.Local ? value.ToUniversalTime() : value;

    private static DateTime AsUtcOnRead(DateTime value) =>
        DateTime.SpecifyKind(value, DateTimeKind.Utc);

    private static DateTime? ToUtcForWriteNullable(DateTime? value) =>
        value.HasValue ? ToUtcForWrite(value.Value) : value;

    private static DateTime? AsUtcOnReadNullable(DateTime? value) =>
        value.HasValue ? AsUtcOnRead(value.Value) : value;

    // Everything below is the audit trail: every insert, update or delete
    // anywhere in this context is recorded to AuditLogs without each
    // controller having to remember to do it. Two exclusions -
    // - AuditLog itself, or logging one would try to log itself.
    // - StockMovement, which already is a complete record of what happened
    //   to stock (see its own comment on OnModelCreating); duplicating each
    //   insert here would say nothing an AuditLog row can that the movement
    //   itself does not already.
    public override int SaveChanges()
    {
        var (readyLogs, pendingAdds) = CaptureAudits();
        var result = base.SaveChanges();
        FinalizeAndPersistAudits(readyLogs, pendingAdds);
        return result;
    }

    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        var (readyLogs, pendingAdds) = CaptureAudits();
        var result = await base.SaveChangesAsync(cancellationToken);
        await FinalizeAndPersistAuditsAsync(readyLogs, pendingAdds, cancellationToken);
        return result;
    }

    // A password hash is not a plaintext credential, but it is still a secret
    // worth keeping out of a table with wider read access than the Users row
    // it came from - what matters for the audit trail is that it changed, not
    // to what.
    //
    // The same goes for everything two-factor. A TOTP secret is reversible by
    // design (TotpSecretProtector), so writing the ciphertext into a second
    // table is one more place the key has to hold; and a recovery code hash in
    // an audit row is a usable credential to anyone who can read that table,
    // since - unlike a password - there is no password to also need. What the
    // trail should say is that someone's second factor changed, and when, which
    // the row still says without any of this in it.
    private static readonly HashSet<string> RedactedProperties = new(StringComparer.Ordinal)
    {
        "Password",
        "TotpSecret",
        "TotpPendingSecret",
        "CodeHash"
    };

    private (List<AuditLog> ReadyLogs, List<(AuditLog Log, EntityEntry Entry)> PendingAdds) CaptureAudits()
    {
        var readyLogs = new List<AuditLog>();
        var pendingAdds = new List<(AuditLog, EntityEntry)>();
        var username = CurrentUsername();

        foreach (var entry in ChangeTracker.Entries())
        {
            if (entry.Entity is AuditLog or StockMovement) continue;

            switch (entry.State)
            {
                case EntityState.Added:
                    // Deliberately not filled in here: an identity column's
                    // real value - Id included - does not exist until the
                    // save this row is part of actually runs. Both EntityId
                    // and NewValue are filled in afterward, from the same
                    // entry, in FinalizeAndPersistAudits(Async).
                    var addLog = new AuditLog
                    {
                        Username = username,
                        Entity = entry.Entity.GetType().Name,
                        Action = "Create"
                    };
                    pendingAdds.Add((addLog, entry));
                    readyLogs.Add(addLog);
                    break;

                case EntityState.Modified:
                    var changed = entry.Properties
                        .Where(property => property.IsModified && !Equals(property.OriginalValue, property.CurrentValue))
                        .ToList();

                    // AttachAndUpdate-style code can mark every property
                    // Modified even when nothing actually differs - nothing
                    // here is worth a row saying so.
                    if (changed.Count == 0) break;

                    readyLogs.Add(new AuditLog
                    {
                        Username = username,
                        Entity = entry.Entity.GetType().Name,
                        EntityId = FormatKey(entry),
                        Action = "Update",
                        OldValue = SerializeValues(changed, property => property.OriginalValue),
                        NewValue = SerializeValues(changed, property => property.CurrentValue)
                    });
                    break;

                case EntityState.Deleted:
                    readyLogs.Add(new AuditLog
                    {
                        Username = username,
                        Entity = entry.Entity.GetType().Name,
                        EntityId = FormatKey(entry),
                        Action = "Delete",
                        OldValue = SerializeValues(entry.Properties, property => property.OriginalValue)
                    });
                    break;
            }
        }

        return (readyLogs, pendingAdds);
    }

    // An Added row's key - and any other database-generated column, though
    // none of these entities have one apart from Id - is whatever EF assigned
    // during the SaveChanges call that just finished, unknowable before it
    // since these are all identity columns. Both EntityId and NewValue are
    // filled in now, from the same entry, for that reason.
    private void FinalizeAndPersistAudits(List<AuditLog> readyLogs, List<(AuditLog Log, EntityEntry Entry)> pendingAdds)
    {
        if (readyLogs.Count == 0) return;

        foreach (var (log, entry) in pendingAdds)
        {
            log.EntityId = FormatKey(entry);
            log.NewValue = SerializeValues(entry.Properties, property => property.CurrentValue);
        }

        AuditLogs.AddRange(readyLogs);
        base.SaveChanges();
    }

    private async Task FinalizeAndPersistAuditsAsync(
        List<AuditLog> readyLogs, List<(AuditLog Log, EntityEntry Entry)> pendingAdds, CancellationToken cancellationToken)
    {
        if (readyLogs.Count == 0) return;

        foreach (var (log, entry) in pendingAdds)
        {
            log.EntityId = FormatKey(entry);
            log.NewValue = SerializeValues(entry.Properties, property => property.CurrentValue);
        }

        AuditLogs.AddRange(readyLogs);
        await base.SaveChangesAsync(cancellationToken);
    }

    private static string FormatKey(EntityEntry entry) =>
        string.Join(",", entry.Properties
            .Where(property => property.Metadata.IsPrimaryKey())
            .Select(property => property.CurrentValue?.ToString() ?? ""));

    private static string SerializeValues(IEnumerable<PropertyEntry> properties, Func<PropertyEntry, object?> select)
    {
        var values = new Dictionary<string, object?>();

        foreach (var property in properties)
        {
            var name = property.Metadata.Name;
            values[name] = RedactedProperties.Contains(name) ? "***" : select(property);
        }

        return JsonSerializer.Serialize(values);
    }

    // Startup seeding (SeedFirstAdmin) writes through this same context with
    // no HTTP request behind it, and a test's TestDatabase never supplies an
    // accessor at all - both cases are a real, deliberate change with nobody
    // signed in to attribute it to.
    private string CurrentUsername() =>
        _httpContextAccessor?.HttpContext?.User?.FindFirstValue(ClaimTypes.Name) ?? "system";
}