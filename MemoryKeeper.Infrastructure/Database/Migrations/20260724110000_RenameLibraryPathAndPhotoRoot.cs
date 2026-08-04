using MemoryKeeper.Infrastructure.Database;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MemoryKeeper.Infrastructure.Database.Migrations;

[DbContext(typeof(MemoryKeeperDbContext))]
[Migration("20260724110000_RenameLibraryPathAndPhotoRoot")]
public partial class RenameLibraryPathAndPhotoRoot : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.RenameColumn(
            name: "LibraryPath",
            table: "TB_MEDIA",
            newName: "RelativePath");

        migrationBuilder.RenameColumn(
            name: "RootPath",
            table: "TB_STORAGE",
            newName: "PhotoRoot");

        migrationBuilder.DropIndex(
            name: "IX_TB_STORAGE_RootPath",
            table: "TB_STORAGE");

        migrationBuilder.CreateIndex(
            name: "IX_TB_STORAGE_PhotoRoot",
            table: "TB_STORAGE",
            column: "PhotoRoot");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_TB_STORAGE_PhotoRoot",
            table: "TB_STORAGE");

        migrationBuilder.RenameColumn(
            name: "PhotoRoot",
            table: "TB_STORAGE",
            newName: "RootPath");

        migrationBuilder.RenameColumn(
            name: "RelativePath",
            table: "TB_MEDIA",
            newName: "LibraryPath");

        migrationBuilder.CreateIndex(
            name: "IX_TB_STORAGE_RootPath",
            table: "TB_STORAGE",
            column: "RootPath");
    }
}
