using HorizonNET.Domain.Entities;
using HorizonNET.Domain.Interfaces;
using HorizonNET.Shared.Transfer.DTOs;
using Microsoft.AspNetCore.Mvc;

namespace HorizonNET.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class TasksController(ITaskRepository repo) : ControllerBase
{
    private static TaskResponseDto ToDto(TaskItem t) =>
        new(t.Id, t.Title, t.Description, t.DueDate, t.StartTime, t.EndTime,
            t.Status, t.Priority.ToString(), t.ProjectId, t.Project?.Name,
            t.SortOrder,
            t.ParentTaskId,
            t.SubTasks.Count > 0 ? t.SubTasks.Select(s => ToDto(s)).ToList() : null);

    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var tasks = await repo.GetAllAsync();
        return Ok(tasks.Select(ToDto));
    }

    [HttpGet("project/{projectId:int}")]
    public async Task<IActionResult> GetByProject(int projectId)
    {
        var tasks = await repo.GetByProjectIdAsync(projectId);
        return Ok(tasks.Select(ToDto));
    }

    [HttpGet("inbox")]
    public async Task<IActionResult> GetInbox()
    {
        var tasks = await repo.GetInboxAsync();
        return Ok(tasks.Select(ToDto));
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetById(int id)
    {
        var task = await repo.GetByIdAsync(id);
        if (task is null) return NotFound();
        return Ok(ToDto(task));
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] TaskCreateDto dto)
    {
        var task = new TaskItem
        {
            Title = dto.Title,
            Description = dto.Description,
            DueDate = dto.DueDate,
            StartTime = dto.StartTime,
            EndTime = dto.EndTime,
            Priority = dto.Priority,
            ProjectId = dto.ProjectId,
            ParentTaskId = dto.ParentTaskId,
            Status = dto.Status
        };
        var created = await repo.CreateAsync(task);
        return CreatedAtAction(nameof(GetById), new { id = created.Id }, ToDto(created));
    }

    [HttpPut("reorder")]
    public async Task<IActionResult> Reorder([FromBody] TaskReorderDto dto)
    {
        await repo.ReorderAsync(dto.Status, dto.OrderedTaskIds);
        return NoContent();
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, [FromBody] TaskUpdateDto dto)
    {
        var updated = await repo.UpdateAsync(id, new TaskItem
        {
            Title = dto.Title,
            Description = dto.Description,
            DueDate = dto.DueDate,
            StartTime = dto.StartTime,
            EndTime = dto.EndTime,
            Status = dto.Status,
            Priority = dto.Priority,
            ProjectId = dto.ProjectId
        });
        if (updated is null) return NotFound();
        return Ok(ToDto(updated));
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        var deleted = await repo.DeleteAsync(id);
        if (!deleted) return NotFound();
        return NoContent();
    }
}
