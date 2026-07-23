using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HorizonNET.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddNoteFolders : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "NoteFolderId",
                table: "Notes",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "NoteFolders",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    ParentFolderId = table.Column<int>(type: "INTEGER", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NoteFolders", x => x.Id);
                    table.ForeignKey(
                        name: "FK_NoteFolders_NoteFolders_ParentFolderId",
                        column: x => x.ParentFolderId,
                        principalTable: "NoteFolders",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_Notes_NoteFolderId",
                table: "Notes",
                column: "NoteFolderId");

            migrationBuilder.CreateIndex(
                name: "IX_NoteFolders_ParentFolderId",
                table: "NoteFolders",
                column: "ParentFolderId");

            migrationBuilder.AddForeignKey(
                name: "FK_Notes_NoteFolders_NoteFolderId",
                table: "Notes",
                column: "NoteFolderId",
                principalTable: "NoteFolders",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Notes_NoteFolders_NoteFolderId",
                table: "Notes");

            migrationBuilder.DropTable(
                name: "NoteFolders");

            migrationBuilder.DropIndex(
                name: "IX_Notes_NoteFolderId",
                table: "Notes");

            migrationBuilder.DropColumn(
                name: "NoteFolderId",
                table: "Notes");
        }
    }
}
