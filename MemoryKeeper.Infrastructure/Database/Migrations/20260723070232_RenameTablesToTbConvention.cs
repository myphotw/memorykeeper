using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MemoryKeeper.Infrastructure.Database.Migrations
{
    /// <inheritdoc />
    public partial class RenameTablesToTbConvention : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Media_Storages_StorageId",
                table: "Media");

            migrationBuilder.DropPrimaryKey(
                name: "PK_Storages",
                table: "Storages");

            migrationBuilder.DropPrimaryKey(
                name: "PK_Settings",
                table: "Settings");

            migrationBuilder.DropPrimaryKey(
                name: "PK_Media",
                table: "Media");

            migrationBuilder.RenameTable(
                name: "Storages",
                newName: "TB_STORAGE");

            migrationBuilder.RenameTable(
                name: "Settings",
                newName: "TB_SETTING");

            migrationBuilder.RenameTable(
                name: "Media",
                newName: "TB_MEDIA");

            migrationBuilder.DropIndex(
                name: "IX_Storages_RootPath",
                table: "TB_STORAGE");

            migrationBuilder.DropIndex(
                name: "IX_Storages_Name",
                table: "TB_STORAGE");

            migrationBuilder.DropIndex(
                name: "IX_Settings_Key",
                table: "TB_SETTING");

            migrationBuilder.DropIndex(
                name: "IX_Media_StorageId",
                table: "TB_MEDIA");

            migrationBuilder.DropIndex(
                name: "IX_Media_ContentHash",
                table: "TB_MEDIA");

            migrationBuilder.DropIndex(
                name: "IX_Media_CapturedAt",
                table: "TB_MEDIA");

            migrationBuilder.CreateIndex(
                name: "IX_TB_STORAGE_RootPath",
                table: "TB_STORAGE",
                column: "RootPath");

            migrationBuilder.CreateIndex(
                name: "IX_TB_STORAGE_Name",
                table: "TB_STORAGE",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TB_SETTING_Key",
                table: "TB_SETTING",
                column: "Key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TB_MEDIA_StorageId",
                table: "TB_MEDIA",
                column: "StorageId");

            migrationBuilder.CreateIndex(
                name: "IX_TB_MEDIA_ContentHash",
                table: "TB_MEDIA",
                column: "ContentHash");

            migrationBuilder.CreateIndex(
                name: "IX_TB_MEDIA_CapturedAt",
                table: "TB_MEDIA",
                column: "CapturedAt");

            migrationBuilder.AddPrimaryKey(
                name: "PK_TB_STORAGE",
                table: "TB_STORAGE",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_TB_SETTING",
                table: "TB_SETTING",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_TB_MEDIA",
                table: "TB_MEDIA",
                column: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_TB_MEDIA_TB_STORAGE_StorageId",
                table: "TB_MEDIA",
                column: "StorageId",
                principalTable: "TB_STORAGE",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_TB_MEDIA_TB_STORAGE_StorageId",
                table: "TB_MEDIA");

            migrationBuilder.DropPrimaryKey(
                name: "PK_TB_STORAGE",
                table: "TB_STORAGE");

            migrationBuilder.DropPrimaryKey(
                name: "PK_TB_SETTING",
                table: "TB_SETTING");

            migrationBuilder.DropPrimaryKey(
                name: "PK_TB_MEDIA",
                table: "TB_MEDIA");

            migrationBuilder.RenameTable(
                name: "TB_STORAGE",
                newName: "Storages");

            migrationBuilder.RenameTable(
                name: "TB_SETTING",
                newName: "Settings");

            migrationBuilder.RenameTable(
                name: "TB_MEDIA",
                newName: "Media");

            migrationBuilder.DropIndex(
                name: "IX_TB_STORAGE_RootPath",
                table: "Storages");

            migrationBuilder.CreateIndex(
                name: "IX_Storages_RootPath",
                table: "Storages",
                column: "RootPath");

            migrationBuilder.DropIndex(
                name: "IX_TB_STORAGE_Name",
                table: "Storages");

            migrationBuilder.CreateIndex(
                name: "IX_Storages_Name",
                table: "Storages",
                column: "Name",
                unique: true);

            migrationBuilder.DropIndex(
                name: "IX_TB_SETTING_Key",
                table: "Settings");

            migrationBuilder.CreateIndex(
                name: "IX_Settings_Key",
                table: "Settings",
                column: "Key",
                unique: true);

            migrationBuilder.DropIndex(
                name: "IX_TB_MEDIA_StorageId",
                table: "Media");

            migrationBuilder.CreateIndex(
                name: "IX_Media_StorageId",
                table: "Media",
                column: "StorageId");

            migrationBuilder.DropIndex(
                name: "IX_TB_MEDIA_ContentHash",
                table: "Media");

            migrationBuilder.CreateIndex(
                name: "IX_Media_ContentHash",
                table: "Media",
                column: "ContentHash");

            migrationBuilder.DropIndex(
                name: "IX_TB_MEDIA_CapturedAt",
                table: "Media");

            migrationBuilder.CreateIndex(
                name: "IX_Media_CapturedAt",
                table: "Media",
                column: "CapturedAt");

            migrationBuilder.AddPrimaryKey(
                name: "PK_Storages",
                table: "Storages",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_Settings",
                table: "Settings",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_Media",
                table: "Media",
                column: "Id");

            migrationBuilder.AddForeignKey(
                name: "FK_Media_Storages_StorageId",
                table: "Media",
                column: "StorageId",
                principalTable: "Storages",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
