using MemoryKeeper.Infrastructure.Database;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MemoryKeeper.Infrastructure.Database.Migrations;

[DbContext(typeof(MemoryKeeperDbContext))]
[Migration("20260724020000_AddPhotoTagSystem")]
public partial class AddPhotoTagSystem : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "TB_TAG",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "TEXT", nullable: false),
                Name = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                Color = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                UsageCount = table.Column<int>(type: "INTEGER", nullable: false, defaultValue: 0),
                Source = table.Column<int>(type: "INTEGER", nullable: false, defaultValue: 0),
                CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_TB_TAG", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "TB_MEDIA_TAG",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "TEXT", nullable: false),
                MediaId = table.Column<Guid>(type: "TEXT", nullable: false),
                TagId = table.Column<Guid>(type: "TEXT", nullable: false),
                CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_TB_MEDIA_TAG", x => x.Id);
                table.ForeignKey(
                    name: "FK_TB_MEDIA_TAG_TB_MEDIA_MediaId",
                    column: x => x.MediaId,
                    principalTable: "TB_MEDIA",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "FK_TB_MEDIA_TAG_TB_TAG_TagId",
                    column: x => x.TagId,
                    principalTable: "TB_TAG",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_TB_TAG_Name",
            table: "TB_TAG",
            column: "Name",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_TB_TAG_UsageCount",
            table: "TB_TAG",
            column: "UsageCount");

        migrationBuilder.CreateIndex(
            name: "IX_TB_TAG_Source",
            table: "TB_TAG",
            column: "Source");

        migrationBuilder.CreateIndex(
            name: "IX_TB_MEDIA_TAG_MediaId_TagId",
            table: "TB_MEDIA_TAG",
            columns: new[] { "MediaId", "TagId" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_TB_MEDIA_TAG_TagId",
            table: "TB_MEDIA_TAG",
            column: "TagId");

        migrationBuilder.CreateIndex(
            name: "IX_TB_MEDIA_TAG_MediaId",
            table: "TB_MEDIA_TAG",
            column: "MediaId");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "TB_MEDIA_TAG");
        migrationBuilder.DropTable(name: "TB_TAG");
    }
}
