using MemoryKeeper.Infrastructure.Database;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MemoryKeeper.Infrastructure.Database.Migrations;

[DbContext(typeof(MemoryKeeperDbContext))]
[Migration("20260724030000_AddPinnedTag")]
public partial class AddPinnedTag : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<bool>(
            name: "IsPinned",
            table: "TB_TAG",
            type: "INTEGER",
            nullable: false,
            defaultValue: false);

        migrationBuilder.CreateIndex(
            name: "IX_TB_TAG_IsPinned",
            table: "TB_TAG",
            column: "IsPinned");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_TB_TAG_IsPinned",
            table: "TB_TAG");

        migrationBuilder.DropColumn(
            name: "IsPinned",
            table: "TB_TAG");
    }
}
