using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HorizonNET.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddTaskSeries : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateOnly>(
                name: "SeriesDate",
                table: "Tasks",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SeriesId",
                table: "Tasks",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "SeriesSlotId",
                table: "Tasks",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "TaskSeries",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Title = table.Column<string>(type: "TEXT", maxLength: 300, nullable: false),
                    Description = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    Kind = table.Column<int>(type: "INTEGER", nullable: false),
                    Priority = table.Column<string>(type: "TEXT", nullable: false),
                    ReminderMinutes = table.Column<int>(type: "INTEGER", nullable: true),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    ProjectId = table.Column<int>(type: "INTEGER", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TaskSeries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TaskSeries_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "TaskSeriesSlots",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    SeriesId = table.Column<int>(type: "INTEGER", nullable: false),
                    DayOfWeek = table.Column<int>(type: "INTEGER", nullable: false),
                    StartTime = table.Column<TimeOnly>(type: "TEXT", nullable: false),
                    EndTime = table.Column<TimeOnly>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TaskSeriesSlots", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TaskSeriesSlots_TaskSeries_SeriesId",
                        column: x => x.SeriesId,
                        principalTable: "TaskSeries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Tasks_SeriesId_SeriesSlotId_SeriesDate",
                table: "Tasks",
                columns: new[] { "SeriesId", "SeriesSlotId", "SeriesDate" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Tasks_SeriesSlotId",
                table: "Tasks",
                column: "SeriesSlotId");

            migrationBuilder.CreateIndex(
                name: "IX_TaskSeries_ProjectId",
                table: "TaskSeries",
                column: "ProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_TaskSeriesSlots_SeriesId",
                table: "TaskSeriesSlots",
                column: "SeriesId");

            migrationBuilder.AddForeignKey(
                name: "FK_Tasks_TaskSeriesSlots_SeriesSlotId",
                table: "Tasks",
                column: "SeriesSlotId",
                principalTable: "TaskSeriesSlots",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_Tasks_TaskSeries_SeriesId",
                table: "Tasks",
                column: "SeriesId",
                principalTable: "TaskSeries",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Tasks_TaskSeriesSlots_SeriesSlotId",
                table: "Tasks");

            migrationBuilder.DropForeignKey(
                name: "FK_Tasks_TaskSeries_SeriesId",
                table: "Tasks");

            migrationBuilder.DropTable(
                name: "TaskSeriesSlots");

            migrationBuilder.DropTable(
                name: "TaskSeries");

            migrationBuilder.DropIndex(
                name: "IX_Tasks_SeriesId_SeriesSlotId_SeriesDate",
                table: "Tasks");

            migrationBuilder.DropIndex(
                name: "IX_Tasks_SeriesSlotId",
                table: "Tasks");

            migrationBuilder.DropColumn(
                name: "SeriesDate",
                table: "Tasks");

            migrationBuilder.DropColumn(
                name: "SeriesId",
                table: "Tasks");

            migrationBuilder.DropColumn(
                name: "SeriesSlotId",
                table: "Tasks");
        }
    }
}
