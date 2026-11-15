using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HorizonNET.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddAzureDevOps : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "AzureDevOpsWorkItemId",
                table: "Tasks",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AzureDevOpsProject",
                table: "Projects",
                type: "TEXT",
                maxLength: 256,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "AzureDevOpsConnections",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    OrganizationUrl = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    PersonalAccessToken = table.Column<string>(type: "TEXT", nullable: false),
                    DisplayName = table.Column<string>(type: "TEXT", maxLength: 320, nullable: true),
                    ConnectedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AzureDevOpsConnections", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Tasks_AzureDevOpsWorkItemId",
                table: "Tasks",
                column: "AzureDevOpsWorkItemId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AzureDevOpsConnections");

            migrationBuilder.DropIndex(
                name: "IX_Tasks_AzureDevOpsWorkItemId",
                table: "Tasks");

            migrationBuilder.DropColumn(
                name: "AzureDevOpsWorkItemId",
                table: "Tasks");

            migrationBuilder.DropColumn(
                name: "AzureDevOpsProject",
                table: "Projects");
        }
    }
}
