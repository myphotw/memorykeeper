using MemoryKeeper.Infrastructure.Database;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MemoryKeeper.Infrastructure.Database.Migrations;

[DbContext(typeof(MemoryKeeperDbContext))]
[Migration("20260724010000_AddMediaFavorite")]
public partial class AddMediaFavorite : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<bool>(
            name: "IsFavorite",
            table: "TB_MEDIA",
            type: "INTEGER",
            nullable: false,
            defaultValue: false);

        migrationBuilder.CreateIndex(
            name: "IX_TB_MEDIA_IsFavorite",
            table: "TB_MEDIA",
            column: "IsFavorite");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_TB_MEDIA_IsFavorite",
            table: "TB_MEDIA");

        migrationBuilder.DropColumn(
            name: "IsFavorite",
            table: "TB_MEDIA");
    }
}
