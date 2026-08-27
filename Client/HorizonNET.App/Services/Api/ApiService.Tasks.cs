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

    public Task<TaskResponseDto?> CreateTaskAsync(TaskCreateDto dto) =>
        PostAsync<TaskResponseDto>("api/tasks", dto);

    // Vollersatz – nur für die echten Editoren (Detailseite, Bearbeiten-Dialog). Wer nur
    // ein Anliegen hat, nimmt eines der Teil-Updates darunter.
    public async Task<TaskResponseDto?> UpdateTaskAsync(int id, TaskUpdateDto dto)
    {
        var updated = await PutAsync<TaskResponseDto>($"api/tasks/{id}", dto);
        if (updated is not null) await NotifyTaskChangedAsync(); // Status kann den Timer gestartet/gestoppt haben
        return updated;
    }

    // ── Teil-Updates ───────────────────────────────────────────────────────────
    // Schicken nur das jeweilige Anliegen; alle übrigen Felder bleiben serverseitig
    // unangetastet. Antwort ist der frische Task.

    // completeSubTasks: offene Sub-Tasks bei "Fertig"/"Verworfen" mit abschließen –
    // Ergebnis der Rückfrage (SubTaskCompletionPrompt) an der Aufrufstelle.
    public async Task<TaskResponseDto?> SetTaskStatusAsync(int id, WorkStatus status, bool completeSubTasks = false)
    {
        var updated = await PutAsync<TaskResponseDto>($"api/tasks/{id}/status", new TaskStatusDto(status, completeSubTasks));
        if (updated is not null) await NotifyTaskChangedAsync(); // Status kann den Timer gestartet/gestoppt haben
        return updated;
    }

    public Task<TaskResponseDto?> SetTaskScheduleAsync(int id, DateTime? dueDate, DateTime? startTime, DateTime? endTime) =>
        PutAsync<TaskResponseDto>($"api/tasks/{id}/schedule", new TaskScheduleDto(dueDate, startTime, endTime));

    public Task<TaskResponseDto?> SetTaskProjectAsync(int id, int? projectId) =>
        PutAsync<TaskResponseDto>($"api/tasks/{id}/project", new TaskProjectDto(projectId));

    public async Task<bool> DeleteTaskAsync(int id)
    {
        var deleted = await DeleteAsync($"api/tasks/{id}");
        if (deleted) await NotifyTaskChangedAsync();
        return deleted;
    }

    public Task<bool> RestoreTaskAsync(int id) =>
        PostAsync($"api/tasks/{id}/restore");

    public async Task<bool> ReorderTasksAsync(TaskReorderDto dto)
    {
        var reordered = await PutAsync("api/tasks/reorder", dto);
        // Im Kanban-Board ist das Verschieben in eine Spalte ein Statuswechsel.
        if (reordered) await NotifyTaskChangedAsync();
        return reordered;
    }

    public Task<bool> ReorderSubTasksAsync(List<int> orderedTaskIds) =>
        PutAsync("api/tasks/reorder-subtasks", orderedTaskIds);

    // Reihenfolge der Haupt-Tasks in der Projektliste. Kein NotifyTaskChangedAsync:
    // es ändert sich nur die Position, kein Status – andere Ansichten bleiben gültig.
    public Task<bool> ReorderTaskListAsync(List<int> orderedTaskIds) =>
        PutAsync("api/tasks/reorder-list", orderedTaskIds);

    // ── Zeiterfassung ────────────────────────────────────────────────────────────

    // Start/Stop liefern den aktualisierten Task zurück (Status und Zeiten inklusive).
    public async Task<TaskResponseDto?> StartTimerAsync(int taskId)
    {
        var updated = await PostAsync<TaskResponseDto>($"api/tasks/{taskId}/timer/start");
        if (updated is not null) await NotifyTaskChangedAsync();
        return updated;
    }

    public async Task<TaskResponseDto?> StopTimerAsync(int taskId)
    {
        var updated = await PostAsync<TaskResponseDto>($"api/tasks/{taskId}/timer/stop");
        if (updated is not null) await NotifyTaskChangedAsync();
        return updated;
    }

    // Bleibt ausgeschrieben: Läuft kein Timer, antwortet die API mit 204 (leerer Body) –
    // GetFromJsonAsync würde daran scheitern, deshalb der Umweg über GetAsync.
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

    public Task<TaskTemplateResponseDto?> CreateTaskTemplateAsync(TaskTemplateCreateDto dto) =>
        PostAsync<TaskTemplateResponseDto>("api/tasktemplates", dto);

    public Task<TaskTemplateResponseDto?> UpdateTaskTemplateAsync(int id, TaskTemplateUpdateDto dto) =>
        PutAsync<TaskTemplateResponseDto>($"api/tasktemplates/{id}", dto);

    public Task<bool> DeleteTaskTemplateAsync(int id) =>
        DeleteAsync($"api/tasktemplates/{id}");

    public Task<bool> RestoreTaskTemplateAsync(int id) =>
        PostAsync($"api/tasktemplates/{id}/restore");
}
