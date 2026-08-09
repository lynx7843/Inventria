using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace inventria.Migrations
{
    /// <inheritdoc />
    public partial class RestrictItemDeletion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Stop an item delete from cascading into the stock recorded against it.
            migrationBuilder.DropForeignKey(
                name: "FK_InventoryBalances_Items_ItemId",
                table: "InventoryBalances");

            migrationBuilder.AddForeignKey(
                name: "FK_InventoryBalances_Items_ItemId",
                table: "InventoryBalances",
                column: "ItemId",
                principalTable: "Items",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.CreateIndex(
                name: "IX_StockMovements_ItemId",
                table: "StockMovements",
                column: "ItemId");

            // WITH NOCHECK, written by hand because AddForeignKey always validates.
            // Movements stranded by a delete that happened before this migration
            // point at items that are gone and cannot be repaired - the item they
            // named no longer exists to point back at. A validating constraint
            // would fail to apply until those rows were deleted, and deleting
            // audit history to install a guard against losing audit history is the
            // wrong trade. An unchecked constraint is still enforced against every
            // insert, update and delete from here on, which is all that is needed
            // to stop the damage from growing.
            migrationBuilder.Sql(@"
                ALTER TABLE [StockMovements] WITH NOCHECK
                ADD CONSTRAINT [FK_StockMovements_Items_ItemId]
                FOREIGN KEY ([ItemId]) REFERENCES [Items] ([Id]) ON DELETE NO ACTION;
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_StockMovements_Items_ItemId",
                table: "StockMovements");

            migrationBuilder.DropIndex(
                name: "IX_StockMovements_ItemId",
                table: "StockMovements");

            migrationBuilder.DropForeignKey(
                name: "FK_InventoryBalances_Items_ItemId",
                table: "InventoryBalances");

            migrationBuilder.AddForeignKey(
                name: "FK_InventoryBalances_Items_ItemId",
                table: "InventoryBalances",
                column: "ItemId",
                principalTable: "Items",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
