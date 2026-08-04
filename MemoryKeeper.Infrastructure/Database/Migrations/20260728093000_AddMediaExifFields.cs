using MemoryKeeper.Infrastructure.Database;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MemoryKeeper.Infrastructure.Database.Migrations;

[DbContext(typeof(MemoryKeeperDbContext))]
[Migration("20260728093000_AddMediaExifFields")]
public partial class AddMediaExifFields : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "DateTimeOriginal",
            table: "TB_MEDIA",
            type: "TEXT",
            maxLength: 64,
            nullable: true);

        migrationBuilder.AddColumn<int>(
            name: "Orientation",
            table: "TB_MEDIA",
            type: "INTEGER",
            nullable: true);

        migrationBuilder.AddColumn<int>(
            name: "Width",
            table: "TB_MEDIA",
            type: "INTEGER",
            nullable: true);

        migrationBuilder.AddColumn<int>(
            name: "Height",
            table: "TB_MEDIA",
            type: "INTEGER",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "CameraMaker",
            table: "TB_MEDIA",
            type: "TEXT",
            maxLength: 128,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "CameraModel",
            table: "TB_MEDIA",
            type: "TEXT",
            maxLength: 128,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "Lens",
            table: "TB_MEDIA",
            type: "TEXT",
            maxLength: 128,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "Iso",
            table: "TB_MEDIA",
            type: "TEXT",
            maxLength: 32,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "Exposure",
            table: "TB_MEDIA",
            type: "TEXT",
            maxLength: 64,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "FNumber",
            table: "TB_MEDIA",
            type: "TEXT",
            maxLength: 32,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "FocalLength",
            table: "TB_MEDIA",
            type: "TEXT",
            maxLength: 32,
            nullable: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "DateTimeOriginal", table: "TB_MEDIA");
        migrationBuilder.DropColumn(name: "Orientation", table: "TB_MEDIA");
        migrationBuilder.DropColumn(name: "Width", table: "TB_MEDIA");
        migrationBuilder.DropColumn(name: "Height", table: "TB_MEDIA");
        migrationBuilder.DropColumn(name: "CameraMaker", table: "TB_MEDIA");
        migrationBuilder.DropColumn(name: "CameraModel", table: "TB_MEDIA");
        migrationBuilder.DropColumn(name: "Lens", table: "TB_MEDIA");
        migrationBuilder.DropColumn(name: "Iso", table: "TB_MEDIA");
        migrationBuilder.DropColumn(name: "Exposure", table: "TB_MEDIA");
        migrationBuilder.DropColumn(name: "FNumber", table: "TB_MEDIA");
        migrationBuilder.DropColumn(name: "FocalLength", table: "TB_MEDIA");
    }
}
