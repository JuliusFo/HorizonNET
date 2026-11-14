using HorizonNET.Domain.Entities;
using HorizonNET.Domain.Interfaces;
using HorizonNET.Shared.Transfer.DTOs;
using Microsoft.AspNetCore.Mvc;

namespace HorizonNET.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ReportsController(ITaskRepository tasks) : ControllerBase
{
    // GET api/reports/weekly?weekStart=2026-11-09&workspaceIds=1&workspaceIds=3&withoutWorkspace=true
    // weekStart darf jeder Tag der Woche sein; gezählt wird ab deren Montag.
    [HttpGet("weekly")]
    public async Task<IActionResult> Weekly(
        [FromQuery] DateOnly weekStart,
        [FromQuery] int[]? workspaceIds,
        [FromQuery] bool withoutWorkspace = false)
    {
        var monday = MondayOf(weekStart);
        var data = await tasks.GetWeeklyReportAsync(monday, workspaceIds ?? [], withoutWorkspace);

        return Ok(new WeeklyReportDto(
            monday,
            data.Completed.Select(ToDto).ToList(),
            data.Open.Select(ToDto).ToList()));
    }

    public static DateOnly MondayOf(DateOnly date) =>
        date.AddDays(-(((int)date.DayOfWeek + 6) % 7));

    private static WeeklyReportTaskDto ToDto(TaskItem t) =>
        new(t.Id, t.Title, t.ParentTask?.Title,
            (t.Project ?? t.ParentTask?.Project)?.Name,
            t.Status, t.Priority, t.WaitingFor, t.DueDate, t.CompletedAt);
}
