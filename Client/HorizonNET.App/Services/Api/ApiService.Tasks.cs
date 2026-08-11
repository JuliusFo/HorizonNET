using System.Net.Http.Json;
using HorizonNET.Shared.Transfer.DTOs;
using HorizonNET.Shared.Transfer.Enums;

namespace HorizonNET.App.Services;

// Tasks samt Teil-Updates, Zeiterfassung und Vorlagen.
public partial class ApiService
{
    // ── Tasks ────────────────────────────────────────────────────────────────

    public Task<List<TaskResponseDto>?> GetTasksAsync() =>
        http.GetFromJsonAsync<List<TaskResponseDto>>("api/tasks");

    public Task<List<TaskResponseDto>?> GetTasksByProjectAsync(int projectId) =>
        http.GetFromJsonAsync<List<TaskResponseDto>>($"api/tasks/project/{projectId}");

    public Task<List<TaskResponseDto>?> GetInboxTasksAsync() =>
        http.GetFromJsonAsync<List<TaskResponseDto>>("api/tasks/inbox");

    public Task<TaskResponseDto?> GetTaskAsync(int id) =>
        http.GetFromJsonAsync<TaskResponseDto>($"api/tasks/{id}");

    // Nur Titel und Zuordnung – für Auswahlfelder. Bewusst getrennt von GetTasksAsync:
    // Dessen Antwort trägt an jedem Task die Zeiterfassung mit, was eine Klappliste
    // teuer macht, ohne dass sie etwas davon zeigt.
    public Task<List<TaskOptionDto>?> GetTaskOptionsAsync() =>
        http.GetFromJsonAsync<List<TaskOptionDto>>("api/tasks/options");

    public async Task<TaskResponseDto?> CreateTaskAsync(TaskCreateDto dto)
    {
        var response = await http.PostAsJsonAsync("api/tasks", dto);
        return response.IsSuccessStatusCode
            ? await response.Content.ReadFromJsonAsync<TaskResponseDto>()
            : null;
    }

    // Vollersatz – nur für die echten Editoren (Detailseite, Bearbeiten-Dialog). Wer nur
    // ein Anliegen hat, nimmt eines der Teil-Updates darunter.
    public async Task<TaskResponseDto?> UpdateTaskAsync(int id, TaskUpdateDto dto)
    {
        var response = await http.PutAsJsonAsync($"api/tasks/{id}", dto);
        if (!response.IsSuccessStatusCode) return null;

        var updated = await response.Content.ReadFromJsonAsync<TaskResponseDto>();
        await NotifyTaskChangedAsync(); // Status kann den Timer gestartet/gestoppt haben
        return updated;
    }

    // ── Teil-Updates ───────────────────────────────────────────────────────────
    // Schicken nur das jeweilige Anliegen; alle übrigen Felder bleiben serverseitig
    // unangetastet. Antwort ist der frische Task.

    public async Task<TaskResponseDto?> SetTaskStatusAsync(int id, WorkStatus status)
    {
        var response = await http.PutAsJsonAsync($"api/tasks/{id}/status", new TaskStatusDto(status));
        if (!response.IsSuccessStatusCode) return null;

        var updated = await response.Content.ReadFromJsonAsync<TaskResponseDto>();
        await NotifyTaskChangedAsync(); // Status kann den Timer gestartet/gestoppt haben
        return updated;
    }

    public async Task<TaskResponseDto?> SetTaskScheduleAsync(int id, DateTime? dueDate, DateTime? startTime, DateTime? endTime)
    {
        var response = await http.PutAsJsonAsync($"api/tasks/{id}/schedule",
            new TaskScheduleDto(dueDate, startTime, endTime));
        return response.IsSuccessStatusCode
            ? await response.Content.ReadFromJsonAsync<TaskResponseDto>()
            : null;
    }

    public async Task<TaskResponseDto?> SetTaskProjectAsync(int id, int? projectId)
    {
        var response = await http.PutAsJsonAsync($"api/tasks/{id}/project", new TaskProjectDto(projectId));
        return response.IsSuccessStatusCode
            ? await response.Content.ReadFromJsonAsync<TaskResponseDto>()
            : null;
    }

    public async Task<bool> DeleteTaskAsync(int id)
    {
        var response = await http.DeleteAsync($"api/tasks/{id}");
        if (response.IsSuccessStatusCode) await NotifyTaskChangedAsync();
        return response.IsSuccessStatusCode;
    }

    public async Task<bool> RestoreTaskAsync(int id)
    {
        var response = await http.PostAsync($"api/tasks/{id}/restore", null);
        return response.IsSuccessStatusCode;
    }

    public async Task<bool> ReorderTasksAsync(TaskReorderDto dto)
    {
        var response = await http.PutAsJsonAsync("api/tasks/reorder", dto);
        // Im Kanban-Board ist das Verschieben in eine Spalte ein Statuswechsel.
        if (response.IsSuccessStatusCode) await NotifyTaskChangedAsync();
        return response.IsSuccessStatusCode;
    }

    public async Task<bool> ReorderSubTasksAsync(List<int> orderedTaskIds)
    {
        var response = await http.PutAsJsonAsync("api/tasks/reorder-subtasks", orderedTaskIds);
        return response.IsSuccessStatusCode;
    }

    // Reihenfolge der Haupt-Tasks in der Projektliste. Kein NotifyTaskChangedAsync:
    // es ändert sich nur die Position, kein Status – andere Ansichten bleiben gültig.
    public async Task<bool> ReorderTaskListAsync(List<int> orderedTaskIds)
    {
        var response = await http.PutAsJsonAsync("api/tasks/reorder-list", orderedTaskIds);
        return response.IsSuccessStatusCode;
    }

    // ── Zeiterfassung ────────────────────────────────────────────────────────────

    // Start/Stop liefern den aktualisierten Task zurück (Status und Zeiten inklusive).
    public async Task<TaskResponseDto?> StartTimerAsync(int taskId)
    {
        var response = await http.PostAsync($"api/tasks/{taskId}/timer/start", null);
        if (!response.IsSuccessStatusCode) return null;

        var updated = await response.Content.ReadFromJsonAsync<TaskResponseDto>();
        await NotifyTaskChangedAsync();
        return updated;
    }

    public async Task<TaskResponseDto?> StopTimerAsync(int taskId)
    {
        var response = await http.PostAsync($"api/tasks/{taskId}/timer/stop", null);
        if (!response.IsSuccessStatusCode) return null;

        var updated = await response.Content.ReadFromJsonAsync<TaskResponseDto>();
        await NotifyTaskChangedAsync();
        return updated;
    }

    // Läuft kein Timer, antwortet die API mit 204 (leerer Body) – GetFromJsonAsync
    // würde daran scheitern, deshalb der Umweg über GetAsync.
    public async Task<RunningTimerDto?> GetRunningTimerAsync()
    {
        var response = await http.GetAsync("api/tasks/timer/running");
        if (!response.IsSuccessStatusCode) return null;
        if (response.StatusCode == System.Net.HttpStatusCode.NoContent) return null;
        if (response.Content.Headers.ContentLength is 0 or null) return null;

        return await response.Content.ReadFromJsonAsync<RunningTimerDto>();
    }

    public Task<List<TimeEntryResponseDto>?> GetTimeEntriesAsync(int taskId) =>
        http.GetFromJsonAsync<List<TimeEntryResponseDto>>($"api/tasks/{taskId}/timeentries");

    // ── Task-Vorlagen ────────────────────────────────────────────────────────────

    public Task<List<TaskTemplateResponseDto>?> GetTaskTemplatesAsync() =>
        http.GetFromJsonAsync<List<TaskTemplateResponseDto>>("api/tasktemplates");

    public async Task<TaskTemplateResponseDto?> CreateTaskTemplateAsync(TaskTemplateCreateDto dto)
    {
        var response = await http.PostAsJsonAsync("api/tasktemplates", dto);
        return response.IsSuccessStatusCode
            ? await response.Content.ReadFromJsonAsync<TaskTemplateResponseDto>()
            : null;
    }

    public async Task<TaskTemplateResponseDto?> UpdateTaskTemplateAsync(int id, TaskTemplateUpdateDto dto)
    {
        var response = await http.PutAsJsonAsync($"api/tasktemplates/{id}", dto);
        return response.IsSuccessStatusCode
            ? await response.Content.ReadFromJsonAsync<TaskTemplateResponseDto>()
            : null;
    }

    public async Task<bool> DeleteTaskTemplateAsync(int id)
    {
        var response = await http.DeleteAsync($"api/tasktemplates/{id}");
        return response.IsSuccessStatusCode;
    }

    public async Task<bool> RestoreTaskTemplateAsync(int id)
    {
        var response = await http.PostAsync($"api/tasktemplates/{id}/restore", null);
        return response.IsSuccessStatusCode;
    }

}
