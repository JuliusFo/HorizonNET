using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HorizonNET.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddTaskListSortOrder : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ListSortOrder",
                table: "Tasks",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            // Backfill: Ohne Startwerte stünde jeder Task auf 0 und die Projektlisten
            // wären willkürlich sortiert. Vergeben wird pro Projekt die Reihenfolge, die
            // bisher zur Anzeige erzeugt wurde (erledigt nach unten, dann Status-Rang,
            // Priorität absteigend, Titel A-Z) – die Listen sehen also zunächst aus wie
            // gewohnt. Status liegt als Zahl in der Spalte, Priority dagegen als Text
            // (HasConversion<string>), deshalb die beiden unterschiedlichen CASE-Blöcke.
            // Nur Haupt-Tasks: Sub-Tasks ordnen sich weiterhin über SortOrder.
            // Soft-gelöschte Zeilen bekommen bewusst auch einen Wert, damit sie nach
            // dem Wiederherstellen nicht alle auf 0 zusammenfallen.
            migrationBuilder.Sql("""
                WITH ranked AS (
                    SELECT Id,
                           ROW_NUMBER() OVER (
                               PARTITION BY ProjectId
                               ORDER BY
                                   CASE WHEN Status IN (5, 6) THEN 1 ELSE 0 END,
                                   CASE Status
                                       WHEN 3 THEN 0  -- In Arbeit
                                       WHEN 2 THEN 1  -- Geplant Heute
                                       WHEN 1 THEN 2  -- Geplant Prio
                                       WHEN 0 THEN 3  -- Geplant
                                       WHEN 4 THEN 4  -- Pausiert
                                       WHEN 5 THEN 5  -- Fertig
                                       WHEN 6 THEN 6  -- Abgebrochen
                                       ELSE 7
                                   END,
                                   CASE Priority
                                       WHEN 'High'   THEN 0
                                       WHEN 'Medium' THEN 1
                                       WHEN 'Low'    THEN 2
                                       ELSE 1
                                   END,
                                   Title COLLATE NOCASE
                           ) - 1 AS rn
                    FROM Tasks
                    WHERE ParentTaskId IS NULL
                )
                UPDATE Tasks
                SET ListSortOrder = (SELECT rn FROM ranked WHERE ranked.Id = Tasks.Id)
                WHERE ParentTaskId IS NULL;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ListSortOrder",
                table: "Tasks");
        }
    }
}
