using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NCM3.Migrations
{
    /// <inheritdoc />
    public partial class AddEnablePasswordColumn : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ComplianceResults_RouterConfigurations_ConfigurationId",
                table: "ComplianceResults");

            migrationBuilder.DropForeignKey(
                name: "FK_ComplianceResults_Routers_RouterId",
                table: "ComplianceResults");

            migrationBuilder.AddColumn<string>(
                name: "EnablePassword",
                table: "Routers",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddForeignKey(
                name: "FK_ComplianceResults_RouterConfigurations_ConfigurationId",
                table: "ComplianceResults",
                column: "ConfigurationId",
                principalTable: "RouterConfigurations",
                principalColumn: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_ComplianceResults_Routers_RouterId",
                table: "ComplianceResults",
                column: "RouterId",
                principalTable: "Routers",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ComplianceResults_RouterConfigurations_ConfigurationId",
                table: "ComplianceResults");

            migrationBuilder.DropForeignKey(
                name: "FK_ComplianceResults_Routers_RouterId",
                table: "ComplianceResults");

            migrationBuilder.DropColumn(
                name: "EnablePassword",
                table: "Routers");

            migrationBuilder.AddForeignKey(
                name: "FK_ComplianceResults_RouterConfigurations_ConfigurationId",
                table: "ComplianceResults",
                column: "ConfigurationId",
                principalTable: "RouterConfigurations",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_ComplianceResults_Routers_RouterId",
                table: "ComplianceResults",
                column: "RouterId",
                principalTable: "Routers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
