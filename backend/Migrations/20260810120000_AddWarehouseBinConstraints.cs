using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace inventria.Migrations
{
    /// <inheritdoc />
    public partial class AddWarehouseBinConstraints : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Bounded so the unique index below has something it can index;
            // nvarchar(max) cannot be part of an index key.
            migrationBuilder.AlterColumn<string>(
                name: "Zone",
                table: "WarehouseBins",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)");

            migrationBuilder.AlterColumn<string>(
                name: "Shelf",
                table: "WarehouseBins",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)");

            migrationBuilder.AlterColumn<string>(
                name: "Aisle",
                table: "WarehouseBins",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)");

            // Two rows with the same address are two Ids for one shelf, which
            // splits that shelf's stock across balances nobody can reconcile.
            migrationBuilder.CreateIndex(
                name: "IX_WarehouseBins_Zone_Aisle_Shelf",
                table: "WarehouseBins",
                columns: ["Zone", "Aisle", "Shelf"],
                unique: true);

            // Deleting a bin used to cascade into the balances stored in it, so
            // retiring a location would have quietly written off the stock on
            // that shelf. Same trade as the item relationship: refuse the delete.
            migrationBuilder.DropForeignKey(
                name: "FK_InventoryBalances_WarehouseBins_WarehouseBinId",
                table: "InventoryBalances");

            migrationBuilder.AddForeignKey(
                name: "FK_InventoryBalances_WarehouseBins_WarehouseBinId",
                table: "InventoryBalances",
                column: "WarehouseBinId",
                principalTable: "WarehouseBins",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.CreateIndex(
                name: "IX_StockMovements_WarehouseBinId",
                table: "StockMovements",
                column: "WarehouseBinId");

            // WITH NOCHECK, for the same reason as the movement/item constraint:
            // rows written before there was anything stopping a bin from being
            // deleted may already name a bin that is gone, and they cannot be
            // repaired - only deleted, which is the audit history this constraint
            // exists to protect. Unchecked still means enforced from here on.
            migrationBuilder.Sql(@"
                ALTER TABLE [StockMovements] WITH NOCHECK
                ADD CONSTRAINT [FK_StockMovements_WarehouseBins_WarehouseBinId]
                FOREIGN KEY ([WarehouseBinId]) REFERENCES [WarehouseBins] ([Id]) ON DELETE NO ACTION;
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_StockMovements_WarehouseBins_WarehouseBinId",
                table: "StockMovements");

            migrationBuilder.DropIndex(
                name: "IX_StockMovements_WarehouseBinId",
                table: "StockMovements");

            migrationBuilder.DropForeignKey(
                name: "FK_InventoryBalances_WarehouseBins_WarehouseBinId",
                table: "InventoryBalances");

            migrationBuilder.AddForeignKey(
                name: "FK_InventoryBalances_WarehouseBins_WarehouseBinId",
                table: "InventoryBalances",
                column: "WarehouseBinId",
                principalTable: "WarehouseBins",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.DropIndex(
                name: "IX_WarehouseBins_Zone_Aisle_Shelf",
                table: "WarehouseBins");

            migrationBuilder.AlterColumn<string>(
                name: "Zone",
                table: "WarehouseBins",
                type: "nvarchar(max)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(64)",
                oldMaxLength: 64);

            migrationBuilder.AlterColumn<string>(
                name: "Shelf",
                table: "WarehouseBins",
                type: "nvarchar(max)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(32)",
                oldMaxLength: 32);

            migrationBuilder.AlterColumn<string>(
                name: "Aisle",
                table: "WarehouseBins",
                type: "nvarchar(max)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(32)",
                oldMaxLength: 32);
        }
    }
}
