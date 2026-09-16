using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace inventria.Migrations
{
    /// <inheritdoc />
    public partial class AddWarehouse : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_WarehouseBins_Zone_Aisle_Shelf",
                table: "WarehouseBins");

            migrationBuilder.CreateTable(
                name: "Warehouses",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Warehouses", x => x.Id);
                });

            // Every bin that exists before this migration was implicitly in
            // the one building the flat Zone/Aisle/Shelf model assumed. This
            // is that building, made explicit, so those bins have somewhere
            // to point WarehouseId at.
            migrationBuilder.InsertData(
                table: "Warehouses",
                columns: new[] { "Id", "Name" },
                values: new object[] { 1, "Main Warehouse" });

            // The default of 1 is what backfills every existing bin into the
            // warehouse just inserted above - SQL Server applies a column
            // default to existing rows when a NOT NULL column is added with
            // one, so this satisfies the constraint without a separate
            // UPDATE statement. New rows going forward always name their own
            // WarehouseId; this default only ever fires for rows that
            // predate the column.
            migrationBuilder.AddColumn<int>(
                name: "WarehouseId",
                table: "WarehouseBins",
                type: "int",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.CreateIndex(
                name: "IX_WarehouseBins_WarehouseId_Zone_Aisle_Shelf",
                table: "WarehouseBins",
                columns: new[] { "WarehouseId", "Zone", "Aisle", "Shelf" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_WarehouseBins_Warehouses_WarehouseId",
                table: "WarehouseBins",
                column: "WarehouseId",
                principalTable: "Warehouses",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_WarehouseBins_Warehouses_WarehouseId",
                table: "WarehouseBins");

            migrationBuilder.DropIndex(
                name: "IX_WarehouseBins_WarehouseId_Zone_Aisle_Shelf",
                table: "WarehouseBins");

            migrationBuilder.DropColumn(
                name: "WarehouseId",
                table: "WarehouseBins");

            migrationBuilder.DropTable(
                name: "Warehouses");

            migrationBuilder.CreateIndex(
                name: "IX_WarehouseBins_Zone_Aisle_Shelf",
                table: "WarehouseBins",
                columns: new[] { "Zone", "Aisle", "Shelf" },
                unique: true);
        }
    }
}
