using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace inventria.Migrations
{
    /// <inheritdoc />
    public partial class AddInventoryBalanceConcurrency : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_InventoryBalances_ItemId",
                table: "InventoryBalances");

            migrationBuilder.AddColumn<byte[]>(
                name: "RowVersion",
                table: "InventoryBalances",
                type: "rowversion",
                rowVersion: true,
                nullable: false,
                defaultValue: new byte[0]);

            migrationBuilder.CreateIndex(
                name: "IX_InventoryBalances_ItemId_WarehouseBinId",
                table: "InventoryBalances",
                columns: new[] { "ItemId", "WarehouseBinId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_InventoryBalances_ItemId_WarehouseBinId",
                table: "InventoryBalances");

            migrationBuilder.DropColumn(
                name: "RowVersion",
                table: "InventoryBalances");

            migrationBuilder.CreateIndex(
                name: "IX_InventoryBalances_ItemId",
                table: "InventoryBalances",
                column: "ItemId");
        }
    }
}
