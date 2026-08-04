using MemoryKeeper.Infrastructure.Database;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MemoryKeeper.Infrastructure.Database.Migrations;

[DbContext(typeof(MemoryKeeperDbContext))]
[Migration("20260728150000_AddPlaceCanonicalName")]
public partial class AddPlaceCanonicalName : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "CanonicalName",
            table: "TB_PLACE",
            type: "TEXT",
            maxLength: 200,
            nullable: true);

        migrationBuilder.CreateIndex(
            name: "IX_TB_PLACE_GooglePlaceId",
            table: "TB_PLACE",
            column: "GooglePlaceId");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_TB_PLACE_GooglePlaceId",
            table: "TB_PLACE");

        migrationBuilder.DropColumn(
            name: "CanonicalName",
            table: "TB_PLACE");
    }
}
