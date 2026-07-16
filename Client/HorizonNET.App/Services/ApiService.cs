using System.Net.Http.Json;
using HorizonNET.Shared.Transfer.DTOs;
using HorizonNET.Shared.Transfer.Enums;

namespace HorizonNET.App.Services;

// Kapselt alle HTTP-Aufrufe an die HorizonNET-API
public class ApiService(HttpClient http)
{
    // Wird nach jeder Änderung an einem Task ausgelöst. Der TimerState hängt sich hier
    // ein: Ein Statuswechsel startet oder stoppt serverseitig die Zeiterfassung (auch
    // an einem anderen Task), und das erfährt der Client sonst nirgends zentral.
    public event Func<Task>? TaskChanged;

    private Task NotifyTaskChangedAsync() => TaskChanged?.Invoke() ?? Task.CompletedTask;

    // ── Arbeitsbereiche ───────────────────────────────────────────────────────

    public Task<List<WorkspaceResponseDto>?> GetWorkspacesAsync() =>
        http.GetFromJsonAsync<List<WorkspaceResponseDto>>("api/workspaces");

    public Task<WorkspaceResponseDto?> GetWorkspaceAsync(int id) =>
        http.GetFromJsonAsync<WorkspaceResponseDto>($"api/workspaces/{id}");

    public async Task<WorkspaceResponseDto?> CreateWorkspaceAsync(WorkspaceCreateDto dto)
    {
        var response = await http.PostAsJsonAsync("api/workspaces", dto);
        return response.IsSuccessStatusCode
            ? await response.Content.ReadFromJsonAsync<WorkspaceResponseDto>()
            : null;
    }

    public async Task<WorkspaceResponseDto?> UpdateWorkspaceAsync(int id, WorkspaceUpdateDto dto)
    {
        var response = await http.PutAsJsonAsync($"api/workspaces/{id}", dto);
        return response.IsSuccessStatusCode
            ? await response.Content.ReadFromJsonAsync<WorkspaceResponseDto>()
            : null;
    }

    public async Task<bool> DeleteWorkspaceAsync(int id)
    {
        var response = await http.DeleteAsync($"api/workspaces/{id}");
        return response.IsSuccessStatusCode;
    }

    public async Task<bool> RestoreWorkspaceAsync(int id)
    {
        var response = await http.PostAsync($"api/workspaces/{id}/restore", null);
        return response.IsSuccessStatusCode;
    }

    // ── Projekte ────────────────────────────────────────────────────────────

    public Task<List<ProjectResponseDto>?> GetProjectsAsync() =>
        http.GetFromJsonAsync<List<ProjectResponseDto>>("api/projects");

    public Task<ProjectResponseDto?> GetProjectAsync(int id) =>
        http.GetFromJsonAsync<ProjectResponseDto>($"api/projects/{id}");

    public async Task<ProjectResponseDto?> CreateProjectAsync(ProjectCreateDto dto)
    {
        var response = await http.PostAsJsonAsync("api/projects", dto);
        return response.IsSuccessStatusCode
            ? await response.Content.ReadFromJsonAsync<ProjectResponseDto>()
            : null;
    }

    public async Task<ProjectResponseDto?> UpdateProjectAsync(int id, ProjectUpdateDto dto)
    {
        var response = await http.PutAsJsonAsync($"api/projects/{id}", dto);
        return response.IsSuccessStatusCode
            ? await response.Content.ReadFromJsonAsync<ProjectResponseDto>()
            : null;
    }

    public async Task<bool> DeleteProjectAsync(int id)
    {
        var response = await http.DeleteAsync($"api/projects/{id}");
        return response.IsSuccessStatusCode;
    }

    public async Task<bool> RestoreProjectAsync(int id)
    {
        var response = await http.PostAsync($"api/projects/{id}/restore", null);
        return response.IsSuccessStatusCode;
    }

    // ── Tasks ────────────────────────────────────────────────────────────────

    public Task<List<TaskResponseDto>?> GetTasksAsync() =>
        http.GetFromJsonAsync<List<TaskResponseDto>>("api/tasks");

    public Task<List<TaskResponseDto>?> GetTasksByProjectAsync(int projectId) =>
        http.GetFromJsonAsync<List<TaskResponseDto>>($"api/tasks/project/{projectId}");

    public Task<List<TaskResponseDto>?> GetInboxTasksAsync() =>
        http.GetFromJsonAsync<List<TaskResponseDto>>("api/tasks/inbox");

    public Task<TaskResponseDto?> GetTaskAsync(int id) =>
        http.GetFromJsonAsync<TaskResponseDto>($"api/tasks/{id}");

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

    // ── Notizen ────────────────────────────────────────────────────────────────

    public Task<List<NoteListItemDto>?> GetNotesAsync() =>
        http.GetFromJsonAsync<List<NoteListItemDto>>("api/notes");

    public Task<NoteResponseDto?> GetNoteAsync(int id) =>
        http.GetFromJsonAsync<NoteResponseDto>($"api/notes/{id}");

    public Task<List<NoteListItemDto>?> GetNotesByTaskAsync(int taskId) =>
        http.GetFromJsonAsync<List<NoteListItemDto>>($"api/notes/task/{taskId}");

    public Task<List<NoteListItemDto>?> GetNotesByProjectAsync(int projectId) =>
        http.GetFromJsonAsync<List<NoteListItemDto>>($"api/notes/project/{projectId}");

    public async Task<NoteResponseDto?> CreateNoteAsync(NoteCreateDto dto)
    {
        var response = await http.PostAsJsonAsync("api/notes", dto);
        return response.IsSuccessStatusCode
            ? await response.Content.ReadFromJsonAsync<NoteResponseDto>()
            : null;
    }

    public async Task<NoteResponseDto?> UpdateNoteAsync(int id, NoteUpdateDto dto)
    {
        var response = await http.PutAsJsonAsync($"api/notes/{id}", dto);
        return response.IsSuccessStatusCode
            ? await response.Content.ReadFromJsonAsync<NoteResponseDto>()
            : null;
    }

    public async Task<bool> DeleteNoteAsync(int id)
    {
        var response = await http.DeleteAsync($"api/notes/{id}");
        return response.IsSuccessStatusCode;
    }

    public async Task<bool> RestoreNoteAsync(int id)
    {
        var response = await http.PostAsync($"api/notes/{id}/restore", null);
        return response.IsSuccessStatusCode;
    }

    // ── Daily Tasks ──────────────────────────────────────────────────────────────

    public Task<List<DailyTaskResponseDto>?> GetDailyTasksAsync() =>
        http.GetFromJsonAsync<List<DailyTaskResponseDto>>("api/dailytasks");

    public Task<List<DailyTaskResponseDto>?> GetDailyTasksTodayAsync() =>
        http.GetFromJsonAsync<List<DailyTaskResponseDto>>("api/dailytasks/today");

    public async Task<DailyTaskResponseDto?> CreateDailyTaskAsync(DailyTaskCreateDto dto)
    {
        var response = await http.PostAsJsonAsync("api/dailytasks", dto);
        return response.IsSuccessStatusCode
            ? await response.Content.ReadFromJsonAsync<DailyTaskResponseDto>()
            : null;
    }

    public async Task<DailyTaskResponseDto?> UpdateDailyTaskAsync(int id, DailyTaskUpdateDto dto)
    {
        var response = await http.PutAsJsonAsync($"api/dailytasks/{id}", dto);
        return response.IsSuccessStatusCode
            ? await response.Content.ReadFromJsonAsync<DailyTaskResponseDto>()
            : null;
    }

    public async Task<bool> DeleteDailyTaskAsync(int id)
    {
        var response = await http.DeleteAsync($"api/dailytasks/{id}");
        return response.IsSuccessStatusCode;
    }

    public async Task<bool> RestoreDailyTaskAsync(int id)
    {
        var response = await http.PostAsync($"api/dailytasks/{id}/restore", null);
        return response.IsSuccessStatusCode;
    }

    public async Task<bool> ReorderDailyTasksAsync(List<int> orderedIds)
    {
        var response = await http.PutAsJsonAsync("api/dailytasks/reorder", orderedIds);
        return response.IsSuccessStatusCode;
    }

    // Häkchen für einen Tag setzen/entfernen (Datum als yyyy-MM-dd; null = heute serverseitig).
    public async Task<bool> SetDailyTaskCompletionAsync(int id, DateOnly date, bool completed)
    {
        var url = $"api/dailytasks/{id}/complete?date={date:yyyy-MM-dd}";
        var response = completed ? await http.PostAsync(url, null) : await http.DeleteAsync(url);
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

    // ── Globale Suche ──────────────────────────────────────────────────────────

    public Task<List<SearchHitDto>?> SearchAsync(string query) =>
        http.GetFromJsonAsync<List<SearchHitDto>>($"api/search?q={Uri.EscapeDataString(query)}");

    // ── Papierkorb ─────────────────────────────────────────────────────────────

    public Task<List<TrashItemDto>?> GetTrashAsync() =>
        http.GetFromJsonAsync<List<TrashItemDto>>("api/trash");

    // Stellt einen Papierkorb-Eintrag über den typspezifischen Restore-Endpunkt wieder
    // her – der Task-Restore spiegelt dabei serverseitig auch den Google-Termin neu.
    public Task<bool> RestoreTrashItemAsync(string type, int id) => type switch
    {
        TrashItemTypes.Workspace => RestoreWorkspaceAsync(id),
        TrashItemTypes.Project   => RestoreProjectAsync(id),
        TrashItemTypes.Task      => RestoreTaskAsync(id),
        TrashItemTypes.Note      => RestoreNoteAsync(id),
        TrashItemTypes.DailyTask => RestoreDailyTaskAsync(id),
        _ => Task.FromResult(false)
    };

    public async Task<bool> PurgeTrashItemAsync(string type, int id)
    {
        var response = await http.DeleteAsync($"api/trash/{type}/{id}");
        return response.IsSuccessStatusCode;
    }

    public async Task<bool> EmptyTrashAsync()
    {
        var response = await http.DeleteAsync("api/trash");
        return response.IsSuccessStatusCode;
    }

    // ── Version ────────────────────────────────────────────────────────────────

    // Version der laufenden API. Fehler werden geschluckt (→ null), damit die
    // Versatz-Prüfung die App nie stört, wenn die API gerade nicht antwortet.
    public async Task<AppVersionDto?> GetApiVersionAsync()
    {
        try { return await http.GetFromJsonAsync<AppVersionDto>("api/version"); }
        catch { return null; }
    }

    // ── Google-Kalender ────────────────────────────────────────────────────────

    public Task<GoogleStatusDto?> GetGoogleStatusAsync() =>
        http.GetFromJsonAsync<GoogleStatusDto>("api/google/status");

    public async Task<bool> DisconnectGoogleAsync()
    {
        var response = await http.DeleteAsync("api/google");
        return response.IsSuccessStatusCode;
    }

    // Holt die Google-Termine eines Zeitraums. Fehler (z. B. nicht verbunden oder
    // Google nicht erreichbar) werden geschluckt, damit der Kalender trotzdem funktioniert.
    public async Task<List<GoogleEventDto>> GetGoogleEventsAsync(DateTime fromUtc, DateTime toUtc)
    {
        try
        {
            var from = new DateTimeOffset(fromUtc, TimeSpan.Zero).ToString("o");
            var to = new DateTimeOffset(toUtc, TimeSpan.Zero).ToString("o");
            var url = $"api/google/events?from={Uri.EscapeDataString(from)}&to={Uri.EscapeDataString(to)}";
            return await http.GetFromJsonAsync<List<GoogleEventDto>>(url) ?? [];
        }
        catch
        {
            return [];
        }
    }
}
