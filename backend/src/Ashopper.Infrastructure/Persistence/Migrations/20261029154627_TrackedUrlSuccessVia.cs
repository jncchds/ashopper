using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ashopper.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class TrackedUrlSuccessVia : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "LastSuccessVia",
                table: "TrackedUrls",
                type: "nvarchar(16)",
                maxLength: 16,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LastSuccessVia",
                table: "TrackedUrls");
        }
    }
}
