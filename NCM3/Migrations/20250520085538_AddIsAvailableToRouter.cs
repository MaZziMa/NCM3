using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NCM3.Migrations
{
    /// <inheritdoc />
    public partial class AddIsAvailableToRouter : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsAvailable",
                table: "Routers",
                type: "bit",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsAvailable",
                table: "Routers");
        }
    }
}
