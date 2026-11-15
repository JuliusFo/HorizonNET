using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HorizonNET.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddCalDavConnection : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "GoogleEventId",
                table: "Tasks",
                newName: "CalendarEventId");

            migrationBuilder.CreateTable(
                name: "CalDavConnections",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ServerUrl = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    Username = table.Column<string>(type: "TEXT", maxLength: 320, nullable: false),
                    AppPassword = table.Column<string>(type: "TEXT", nullable: false),
                    CalendarUrl = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: false),
                    CalendarName = table.Column<string>(type: "TEXT", maxLength: 320, nullable: true),
                    ConnectedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CalDavConnections", x => x.Id);
                });

            // Die Erinnerungs-Vorlaufzeit gilt jetzt für jeden Kalender-Anbieter.
            migrationBuilder.Sql(
                "UPDATE AppSettings SET Key = 'calendar.reminderMinutes' WHERE Key = 'google.reminderMinutes';");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "UPDATE AppSettings SET Key = 'google.reminderMinutes' WHERE Key = 'calendar.reminderMinutes';");

            migrationBuilder.DropTable(
                name: "CalDavConnections");

            migrationBuilder.RenameColumn(
                name: "CalendarEventId",
                table: "Tasks",
                newName: "GoogleEventId");
        }
    }
}
