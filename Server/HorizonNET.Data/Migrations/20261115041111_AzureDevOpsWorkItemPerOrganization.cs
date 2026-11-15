using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HorizonNET.Data.Migrations
{
    /// <inheritdoc />
    public partial class AzureDevOpsWorkItemPerOrganization : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Tasks_AzureDevOpsWorkItemId",
                table: "Tasks");

            migrationBuilder.AddColumn<string>(
                name: "AzureDevOpsOrganizationUrl",
                table: "Tasks",
                type: "TEXT",
                maxLength: 512,
                nullable: true);

            // Bisherige Imports stammen aus der aktuell verbundenen Organisation (es gab
            // immer nur eine). Ohne Verbindung bleibt die Organisation unbekannt (NULL).
            migrationBuilder.Sql("""
                UPDATE "Tasks"
                SET "AzureDevOpsOrganizationUrl" = (SELECT "OrganizationUrl" FROM "AzureDevOpsConnections" LIMIT 1)
                WHERE "AzureDevOpsWorkItemId" IS NOT NULL;
                """);

            // Dubletten aus parallelen Imports (vor dem eindeutigen Index möglich): Der
            // älteste Task behält die Verknüpfung, die übrigen bleiben als normale Tasks
            // erhalten – sonst schlüge das Anlegen des Index fehl.
            migrationBuilder.Sql("""
                UPDATE "Tasks"
                SET "AzureDevOpsWorkItemId" = NULL
                WHERE "AzureDevOpsWorkItemId" IS NOT NULL
                  AND "Id" <> (
                      SELECT MIN(t2."Id") FROM "Tasks" t2
                      WHERE t2."AzureDevOpsWorkItemId" = "Tasks"."AzureDevOpsWorkItemId"
                        AND t2."AzureDevOpsOrganizationUrl" IS "Tasks"."AzureDevOpsOrganizationUrl");
                """);

            migrationBuilder.CreateIndex(
                name: "IX_Tasks_AzureDevOpsOrganizationUrl_AzureDevOpsWorkItemId",
                table: "Tasks",
                columns: new[] { "AzureDevOpsOrganizationUrl", "AzureDevOpsWorkItemId" },
                unique: true,
                filter: "\"AzureDevOpsWorkItemId\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Tasks_AzureDevOpsOrganizationUrl_AzureDevOpsWorkItemId",
                table: "Tasks");

            migrationBuilder.DropColumn(
                name: "AzureDevOpsOrganizationUrl",
                table: "Tasks");

            migrationBuilder.CreateIndex(
                name: "IX_Tasks_AzureDevOpsWorkItemId",
                table: "Tasks",
                column: "AzureDevOpsWorkItemId");
        }
    }
}
