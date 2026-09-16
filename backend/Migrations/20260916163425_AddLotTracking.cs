using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace inventria.Migrations
{
    /// <inheritdoc />
    public partial class AddLotTracking : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "LotId",
                table: "StockMovements",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "TracksLots",
                table: "Items",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "Lots",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ItemId = table.Column<int>(type: "int", nullable: false),
                    LotNumber = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    ExpirationDate = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Lots", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Lots_Items_ItemId",
                        column: x => x.ItemId,
                        principalTable: "Items",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "InventoryLotBalances",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ItemId = table.Column<int>(type: "int", nullable: false),
                    WarehouseBinId = table.Column<int>(type: "int", nullable: false),
                    LotId = table.Column<int>(type: "int", nullable: false),
                    Quantity = table.Column<int>(type: "int", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InventoryLotBalances", x => x.Id);
                    table.ForeignKey(
                        name: "FK_InventoryLotBalances_Items_ItemId",
                        column: x => x.ItemId,
                        principalTable: "Items",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InventoryLotBalances_Lots_LotId",
                        column: x => x.LotId,
                        principalTable: "Lots",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_InventoryLotBalances_WarehouseBins_WarehouseBinId",
                        column: x => x.WarehouseBinId,
                        principalTable: "WarehouseBins",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_StockMovements_LotId",
                table: "StockMovements",
                column: "LotId");

            migrationBuilder.CreateIndex(
                name: "IX_InventoryLotBalances_ItemId_WarehouseBinId_LotId",
                table: "InventoryLotBalances",
                columns: new[] { "ItemId", "WarehouseBinId", "LotId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_InventoryLotBalances_LotId",
                table: "InventoryLotBalances",
                column: "LotId");

            migrationBuilder.CreateIndex(
                name: "IX_InventoryLotBalances_WarehouseBinId",
                table: "InventoryLotBalances",
                column: "WarehouseBinId");

            migrationBuilder.CreateIndex(
                name: "IX_Lots_ItemId_LotNumber",
                table: "Lots",
                columns: new[] { "ItemId", "LotNumber" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_StockMovements_Lots_LotId",
                table: "StockMovements",
                column: "LotId",
                principalTable: "Lots",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_StockMovements_Lots_LotId",
                table: "StockMovements");

            migrationBuilder.DropTable(
                name: "InventoryLotBalances");

            migrationBuilder.DropTable(
                name: "Lots");

            migrationBuilder.DropIndex(
                name: "IX_StockMovements_LotId",
                table: "StockMovements");

            migrationBuilder.DropColumn(
                name: "LotId",
                table: "StockMovements");

            migrationBuilder.DropColumn(
                name: "TracksLots",
                table: "Items");
        }
    }
}
