using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NCM3.Migrations
{
    /// <inheritdoc />
    public partial class AddRouterGroup : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Group",
                table: "Routers",
                type: "nvarchar(max)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Group",
                table: "Routers");
        }
    }
}
