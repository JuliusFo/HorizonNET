using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HorizonNET.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddTaskCompletedAt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "CompletedAt",
                table: "Tasks",
                type: "TEXT",
                nullable: true);

            // Bestandsdaten näherungsweise befüllen: Für bereits erledigte Tasks gibt es
            // keinen echten Erledigt-Zeitpunkt mehr, UpdatedAt ist die beste verfügbare
            // Schätzung. Ab jetzt setzt ApplyStatusChangeAsync den Wert exakt.
            //
            // TaskItem.Status liegt als ZAHL in der Spalte – anders als TaskItem.Priority
            // und Project.Status hat es keine HasConversion<string>. 5 = Done, 6 = Abandoned
            // (siehe WorkStatus). Die Werte sind hier hart eingesetzt, weil eine Migration
            // den Stand von damals festhalten muss und nicht mit dem Enum mitwandern darf.
            migrationBuilder.Sql("""
                UPDATE Tasks
                SET CompletedAt = UpdatedAt
                WHERE Status IN (5, 6) AND CompletedAt IS NULL;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CompletedAt",
                table: "Tasks");
        }
    }
}
