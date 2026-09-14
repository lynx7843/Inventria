using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace inventria.Migrations
{
    /// <inheritdoc />
    public partial class AddItemUnitOfMeasure : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "UnitOfMeasure",
                table: "Items",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "unit");

            migrationBuilder.AddColumn<int>(
                name: "UnitsPerPack",
                table: "Items",
                type: "int",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "UnitOfMeasure",
                table: "Items");

            migrationBuilder.DropColumn(
                name: "UnitsPerPack",
                table: "Items");
        }
    }
}
