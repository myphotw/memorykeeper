using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MemoryKeeper.Infrastructure.Database.Migrations
{
    /// <inheritdoc />
    public partial class AddPlaceEntity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "PlaceId",
                table: "TB_MEDIA",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "TB_PLACE",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    DisplayName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Country = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Province = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    City = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Address = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    Latitude = table.Column<double>(type: "REAL", nullable: false),
                    Longitude = table.Column<double>(type: "REAL", nullable: false),
                    Radius = table.Column<double>(type: "REAL", nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TB_PLACE", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TB_MEDIA_PlaceId",
                table: "TB_MEDIA",
                column: "PlaceId");

            migrationBuilder.CreateIndex(
                name: "IX_TB_PLACE_DisplayName",
                table: "TB_PLACE",
                column: "DisplayName");

            migrationBuilder.CreateIndex(
                name: "IX_TB_PLACE_IsActive",
                table: "TB_PLACE",
                column: "IsActive");

            migrationBuilder.CreateIndex(
                name: "IX_TB_PLACE_Latitude_Longitude",
                table: "TB_PLACE",
                columns: new[] { "Latitude", "Longitude" });

            migrationBuilder.AddForeignKey(
                name: "FK_TB_MEDIA_TB_PLACE_PlaceId",
                table: "TB_MEDIA",
                column: "PlaceId",
                principalTable: "TB_PLACE",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_TB_MEDIA_TB_PLACE_PlaceId",
                table: "TB_MEDIA");

            migrationBuilder.DropTable(
                name: "TB_PLACE");

            migrationBuilder.DropIndex(
                name: "IX_TB_MEDIA_PlaceId",
                table: "TB_MEDIA");

            migrationBuilder.DropColumn(
                name: "PlaceId",
                table: "TB_MEDIA");
        }
    }
}
