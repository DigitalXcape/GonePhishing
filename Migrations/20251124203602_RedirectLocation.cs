using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GonePhishing.Migrations
{
    /// <inheritdoc />
    public partial class RedirectLocation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "RedirectLocation",
                table: "DomainTasks",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RedirectLocation",
                table: "DomainTasks");
        }
    }
}
