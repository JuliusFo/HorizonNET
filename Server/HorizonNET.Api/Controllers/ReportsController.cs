using HorizonNET.Domain.Entities;
using HorizonNET.Domain.Interfaces;
using HorizonNET.Shared.Transfer.DTOs;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace HorizonNET.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ReportsController(ITaskRepository tasks) : ControllerBase
{
    // GET api/reports/weekly?weekStart=2026-11-09&workspaceIds=1&workspaceIds=3&withoutWorkspace=true
    // weekStart darf jeder Tag der Woche sein; gezählt wird ab deren Montag. Pflicht –
    // ohne ihn fragte die API stillschweigend die Woche ab 0001-01-01 ab – und auf einen
    // Bereich begrenzt, in dem Montag und Wochenende als DateTime darstellbar sind.
    [HttpGet("weekly")]
    public async Task<IActionResult> Weekly(
        [FromQuery, BindRequired] DateOnly weekStart,
        [FromQuery] int[]? workspaceIds,
        [FromQuery] bool withoutWorkspace = false)
    {
        if (weekStart < MinWeekStart || weekStart > MaxWeekStart)
            return BadRequest($"weekStart muss zwischen {MinWeekStart:yyyy-MM-dd} und {MaxWeekStart:yyyy-MM-dd} liegen.");

        var monday = MondayOf(weekStart);
        var data = await tasks.GetWeeklyReportAsync(monday, workspaceIds ?? [], withoutWorkspace);

        return Ok(new WeeklyReportDto(
            monday,
            data.Completed.Select(ToDto).ToList(),
            data.Open.Select(ToDto).ToList()));
    }

    private static readonly DateOnly MinWeekStart = new(2000, 1, 1);
    private static readonly DateOnly MaxWeekStart = new(9000, 12, 31);

    public static DateOnly MondayOf(DateOnly date) =>
        date.AddDays(-(((int)date.DayOfWeek + 6) % 7));

    private static WeeklyReportTaskDto ToDto(TaskItem t) =>
        new(t.Id, t.Title, t.ParentTask?.Title,
            (t.Project ?? t.ParentTask?.Project)?.Name,
            t.Status, t.Priority, t.WaitingFor, t.DueDate, t.CompletedAt);
}
