using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BudgetApp.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddScanKind : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Kind",
                table: "ReceiptScans",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Kind",
                table: "ReceiptScans");
        }
    }
}
