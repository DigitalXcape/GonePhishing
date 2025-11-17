using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GonePhishing.Migrations
{
    /// <inheritdoc />
    public partial class RiskScore : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "ContainsBrandKeywords",
                table: "DomainTasks",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "ContainsSuspiciousForms",
                table: "DomainTasks",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "HasObfuscatedScripts",
                table: "DomainTasks",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "HtmlScore",
                table: "DomainTasks",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "HtmlTextPreview",
                table: "DomainTasks",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "HtmlTitle",
                table: "DomainTasks",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "RiskLevel",
                table: "DomainTasks",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "TotalRiskScore",
                table: "DomainTasks",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ContainsBrandKeywords",
                table: "DomainTasks");

            migrationBuilder.DropColumn(
                name: "ContainsSuspiciousForms",
                table: "DomainTasks");

            migrationBuilder.DropColumn(
                name: "HasObfuscatedScripts",
                table: "DomainTasks");

            migrationBuilder.DropColumn(
                name: "HtmlScore",
                table: "DomainTasks");

            migrationBuilder.DropColumn(
                name: "HtmlTextPreview",
                table: "DomainTasks");

            migrationBuilder.DropColumn(
                name: "HtmlTitle",
                table: "DomainTasks");

            migrationBuilder.DropColumn(
                name: "RiskLevel",
                table: "DomainTasks");

            migrationBuilder.DropColumn(
                name: "TotalRiskScore",
                table: "DomainTasks");
        }
    }
}
