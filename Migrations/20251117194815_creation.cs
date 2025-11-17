using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GonePhishing.Migrations
{
    /// <inheritdoc />
    public partial class creation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ScanJobs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Owner = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    SeedDomains = table.Column<string>(type: "TEXT", nullable: false),
                    NumberOfTypoDomains = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ScanJobs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "DomainTasks",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ScanJobId = table.Column<int>(type: "INTEGER", nullable: false),
                    CandidateDomain = table.Column<string>(type: "TEXT", nullable: false),
                    BaseDomain = table.Column<string>(type: "TEXT", nullable: false),
                    IPAddresses = table.Column<string>(type: "TEXT", nullable: true),
                    HttpStatus = table.Column<int>(type: "INTEGER", nullable: true),
                    HttpReason = table.Column<string>(type: "TEXT", nullable: true),
                    HtmlScore = table.Column<int>(type: "INTEGER", nullable: true),
                    HtmlTitle = table.Column<string>(type: "TEXT", nullable: true),
                    HtmlTextPreview = table.Column<string>(type: "TEXT", nullable: true),
                    ContainsSuspiciousForms = table.Column<bool>(type: "INTEGER", nullable: true),
                    ContainsBrandKeywords = table.Column<bool>(type: "INTEGER", nullable: true),
                    HasObfuscatedScripts = table.Column<bool>(type: "INTEGER", nullable: true),
                    TotalRiskScore = table.Column<int>(type: "INTEGER", nullable: true),
                    RiskLevel = table.Column<int>(type: "INTEGER", nullable: true),
                    LookUpStatus = table.Column<int>(type: "INTEGER", nullable: false),
                    State = table.Column<int>(type: "INTEGER", nullable: false),
                    Error = table.Column<string>(type: "TEXT", nullable: true),
                    ProcessedAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DomainTasks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DomainTasks_ScanJobs_ScanJobId",
                        column: x => x.ScanJobId,
                        principalTable: "ScanJobs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DomainTasks_ScanJobId",
                table: "DomainTasks",
                column: "ScanJobId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DomainTasks");

            migrationBuilder.DropTable(
                name: "ScanJobs");
        }
    }
}
