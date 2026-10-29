using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ashopper.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PoliteScraper : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ScrapeHosts",
                columns: table => new
                {
                    Host = table.Column<string>(type: "nvarchar(253)", maxLength: 253, nullable: false),
                    NextRequestAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    BlockedUntil = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    BlockReason = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    RobotsTxt = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RobotsFetchedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ScrapeHosts", x => x.Host);
                });

            migrationBuilder.CreateTable(
                name: "TrackedUrls",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Url = table.Column<string>(type: "nvarchar(2048)", maxLength: 2048, nullable: false),
                    UrlHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    StoreKey = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    CategoryId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProductId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    OfferId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    NextCheckAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    LastCheckedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    LastSuccessAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    LastStatus = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    LastError = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    ConsecutiveFailures = table.Column<int>(type: "int", nullable: false),
                    Title = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TrackedUrls", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TrackedUrls_Categories_CategoryId",
                        column: x => x.CategoryId,
                        principalTable: "Categories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TrackedUrls_Offers_OfferId",
                        column: x => x.OfferId,
                        principalTable: "Offers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_TrackedUrls_Products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "TrackedUrlSubscriptions",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TrackedUrlId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AddedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TrackedUrlSubscriptions", x => new { x.UserId, x.TrackedUrlId });
                    table.ForeignKey(
                        name: "FK_TrackedUrlSubscriptions_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_TrackedUrlSubscriptions_TrackedUrls_TrackedUrlId",
                        column: x => x.TrackedUrlId,
                        principalTable: "TrackedUrls",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TrackedUrls_CategoryId",
                table: "TrackedUrls",
                column: "CategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_TrackedUrls_NextCheckAt",
                table: "TrackedUrls",
                column: "NextCheckAt");

            migrationBuilder.CreateIndex(
                name: "IX_TrackedUrls_OfferId",
                table: "TrackedUrls",
                column: "OfferId");

            migrationBuilder.CreateIndex(
                name: "IX_TrackedUrls_ProductId",
                table: "TrackedUrls",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_TrackedUrls_UrlHash",
                table: "TrackedUrls",
                column: "UrlHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TrackedUrlSubscriptions_TrackedUrlId",
                table: "TrackedUrlSubscriptions",
                column: "TrackedUrlId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ScrapeHosts");

            migrationBuilder.DropTable(
                name: "TrackedUrlSubscriptions");

            migrationBuilder.DropTable(
                name: "TrackedUrls");
        }
    }
}
