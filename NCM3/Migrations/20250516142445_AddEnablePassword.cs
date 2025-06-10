using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NCM3.Migrations
{
    /// <inheritdoc />
    public partial class AddEnablePassword : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "EnablePassword",
                table: "Routers",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ComplianceRules",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Pattern = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Severity = table.Column<int>(type: "int", nullable: false),
                    DeviceType = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ExpectedResult = table.Column<bool>(type: "bit", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    Notes = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ComplianceRules", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ConfigTemplates",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Content = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    DeviceType = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreatedDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Version = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ConfigTemplates", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ComplianceResults",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    RouterId = table.Column<int>(type: "int", nullable: false),
                    ConfigurationId = table.Column<int>(type: "int", nullable: false),
                    RuleId = table.Column<int>(type: "int", nullable: false),
                    Result = table.Column<bool>(type: "bit", nullable: false),
                    IsCompliant = table.Column<bool>(type: "bit", nullable: false),
                    LineNumber = table.Column<int>(type: "int", nullable: true),
                    MatchedContent = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CheckDate = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ComplianceResults", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ComplianceResults_ComplianceRules_RuleId",
                        column: x => x.RuleId,
                        principalTable: "ComplianceRules",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ComplianceResults_RouterConfigurations_ConfigurationId",
                        column: x => x.ConfigurationId,
                        principalTable: "RouterConfigurations",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_ComplianceResults_Routers_RouterId",
                        column: x => x.RouterId,
                        principalTable: "Routers",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_ComplianceResults_ConfigurationId",
                table: "ComplianceResults",
                column: "ConfigurationId");

            migrationBuilder.CreateIndex(
                name: "IX_ComplianceResults_RouterId",
                table: "ComplianceResults",
                column: "RouterId");

            migrationBuilder.CreateIndex(
                name: "IX_ComplianceResults_RuleId",
                table: "ComplianceResults",
                column: "RuleId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ComplianceResults");

            migrationBuilder.DropTable(
                name: "ConfigTemplates");

            migrationBuilder.DropTable(
                name: "ComplianceRules");

            migrationBuilder.DropColumn(
                name: "EnablePassword",
                table: "Routers");
        }
    }
}
