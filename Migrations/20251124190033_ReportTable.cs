using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GonePhishing.Migrations
{
    /// <inheritdoc />
    public partial class ReportTable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "RiskReasons",
                table: "DomainTasks",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ScanJobReports",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    TypoDomain = table.Column<string>(type: "TEXT", nullable: false),
                    Reasons = table.Column<string>(type: "TEXT", nullable: false),
                    ScannedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ScanJobId = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ScanJobReports", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ScanJobReports_ScanJobs_ScanJobId",
                        column: x => x.ScanJobId,
                        principalTable: "ScanJobs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ScanJobReports_ScanJobId",
                table: "ScanJobReports",
                column: "ScanJobId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ScanJobReports");

            migrationBuilder.DropColumn(
                name: "RiskReasons",
                table: "DomainTasks");
        }
    }
}
