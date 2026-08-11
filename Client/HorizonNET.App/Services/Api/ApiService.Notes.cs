using System.Net.Http.Json;
using HorizonNET.Shared.Transfer.DTOs;

namespace HorizonNET.App.Services;

// Notizen und ihre Ordner.
public partial class ApiService
{
    // ── Notiz-Ordner (manuelle Ablage) ─────────────────────────────────────────

    public Task<List<NoteFolderResponseDto>?> GetNoteFoldersAsync() =>
        http.GetFromJsonAsync<List<NoteFolderResponseDto>>("api/note-folders");

    public Task<NoteFolderResponseDto?> CreateNoteFolderAsync(NoteFolderCreateDto dto) =>
        PostAsync<NoteFolderResponseDto>("api/note-folders", dto);

    public Task<NoteFolderResponseDto?> RenameNoteFolderAsync(int id, string name) =>
        PutAsync<NoteFolderResponseDto>($"api/note-folders/{id}/name", new NoteFolderRenameDto(name));

    // null = auf die oberste Ebene. Liefert null, wenn das Ziel ein Nachfahre wäre.
    public Task<NoteFolderResponseDto?> MoveNoteFolderAsync(int id, int? parentFolderId) =>
        PutAsync<NoteFolderResponseDto>($"api/note-folders/{id}/parent", new NoteFolderMoveDto(parentFolderId));

    public Task<bool> DeleteNoteFolderAsync(int id) =>
        DeleteAsync($"api/note-folders/{id}");

    public Task<bool> RestoreNoteFolderAsync(int id) =>
        PostAsync($"api/note-folders/{id}/restore");

    // ── Notizen ────────────────────────────────────────────────────────────────

    public Task<List<NoteListItemDto>?> GetNotesAsync() =>
        http.GetFromJsonAsync<List<NoteListItemDto>>("api/notes");

    public Task<NoteResponseDto?> GetNoteAsync(int id) =>
        http.GetFromJsonAsync<NoteResponseDto>($"api/notes/{id}");

    public Task<List<NoteListItemDto>?> GetNotesByTaskAsync(int taskId) =>
        http.GetFromJsonAsync<List<NoteListItemDto>>($"api/notes/task/{taskId}");

    public Task<List<NoteListItemDto>?> GetNotesByProjectAsync(int projectId) =>
        http.GetFromJsonAsync<List<NoteListItemDto>>($"api/notes/project/{projectId}");

    // Nur die direkt am Arbeitsbereich hängenden Notizen – nicht die seiner Projekte.
    public Task<List<NoteListItemDto>?> GetNotesByWorkspaceAsync(int workspaceId) =>
        http.GetFromJsonAsync<List<NoteListItemDto>>($"api/notes/workspace/{workspaceId}");

    public Task<NoteResponseDto?> CreateNoteAsync(NoteCreateDto dto) =>
        PostAsync<NoteResponseDto>("api/notes", dto);

    public Task<NoteResponseDto?> UpdateNoteAsync(int id, NoteUpdateDto dto) =>
        PutAsync<NoteResponseDto>($"api/notes/{id}", dto);

    public Task<bool> DeleteNoteAsync(int id) =>
        DeleteAsync($"api/notes/{id}");

    public Task<bool> RestoreNoteAsync(int id) =>
        PostAsync($"api/notes/{id}/restore");
}
