using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HorizonNET.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddNoteWorkspace : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "WorkspaceId",
                table: "Notes",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Notes_WorkspaceId",
                table: "Notes",
                column: "WorkspaceId");

            migrationBuilder.AddForeignKey(
                name: "FK_Notes_Workspaces_WorkspaceId",
                table: "Notes",
                column: "WorkspaceId",
                principalTable: "Workspaces",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Notes_Workspaces_WorkspaceId",
                table: "Notes");

            migrationBuilder.DropIndex(
                name: "IX_Notes_WorkspaceId",
                table: "Notes");

            migrationBuilder.DropColumn(
                name: "WorkspaceId",
                table: "Notes");
        }
    }
}
