using MemoryKeeper.Infrastructure.Database;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MemoryKeeper.Infrastructure.Database.Migrations;

[DbContext(typeof(MemoryKeeperDbContext))]
[Migration("20260728103000_AddPlaceMasterDataFields")]
public partial class AddPlaceMasterDataFields : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "PostalCode",
            table: "TB_PLACE",
            type: "TEXT",
            maxLength: 32,
            nullable: false,
            defaultValue: "");

        migrationBuilder.AddColumn<string>(
            name: "GooglePlaceId",
            table: "TB_PLACE",
            type: "TEXT",
            maxLength: 128,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "Category",
            table: "TB_PLACE",
            type: "TEXT",
            maxLength: 64,
            nullable: true);

        migrationBuilder.AddColumn<bool>(
            name: "IsFavorite",
            table: "TB_PLACE",
            type: "INTEGER",
            nullable: false,
            defaultValue: false);

        migrationBuilder.AddColumn<int>(
            name: "UsageCount",
            table: "TB_PLACE",
            type: "INTEGER",
            nullable: false,
            defaultValue: 0);

        migrationBuilder.AddColumn<DateTime>(
            name: "LastUsedAt",
            table: "TB_PLACE",
            type: "TEXT",
            nullable: true);

        migrationBuilder.CreateIndex(
            name: "IX_TB_PLACE_IsFavorite",
            table: "TB_PLACE",
            column: "IsFavorite");

        migrationBuilder.CreateIndex(
            name: "IX_TB_PLACE_LastUsedAt",
            table: "TB_PLACE",
            column: "LastUsedAt");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(name: "IX_TB_PLACE_IsFavorite", table: "TB_PLACE");
        migrationBuilder.DropIndex(name: "IX_TB_PLACE_LastUsedAt", table: "TB_PLACE");
        migrationBuilder.DropColumn(name: "PostalCode", table: "TB_PLACE");
        migrationBuilder.DropColumn(name: "GooglePlaceId", table: "TB_PLACE");
        migrationBuilder.DropColumn(name: "Category", table: "TB_PLACE");
        migrationBuilder.DropColumn(name: "IsFavorite", table: "TB_PLACE");
        migrationBuilder.DropColumn(name: "UsageCount", table: "TB_PLACE");
        migrationBuilder.DropColumn(name: "LastUsedAt", table: "TB_PLACE");
    }
}
