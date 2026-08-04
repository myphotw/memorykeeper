using MemoryKeeper.Infrastructure.Database;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MemoryKeeper.Infrastructure.Database.Migrations;

[DbContext(typeof(MemoryKeeperDbContext))]
[Migration("20260728170000_AddMediaMemo")]
public partial class AddMediaMemo : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "Memo",
            table: "TB_MEDIA",
            type: "TEXT",
            maxLength: 4000,
            nullable: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "Memo",
            table: "TB_MEDIA");
    }
}
