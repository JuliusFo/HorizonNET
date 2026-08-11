using System.Net.Http.Json;
using HorizonNET.Shared.Transfer.DTOs;
using HorizonNET.Shared.Transfer.Enums;

namespace HorizonNET.App.Services;

// Notizen und ihre Ordner.
public partial class ApiService
{
    // ── Notiz-Ordner (manuelle Ablage) ─────────────────────────────────────────

    public Task<List<NoteFolderResponseDto>?> GetNoteFoldersAsync() =>
        http.GetFromJsonAsync<List<NoteFolderResponseDto>>("api/note-folders");

    public async Task<NoteFolderResponseDto?> CreateNoteFolderAsync(NoteFolderCreateDto dto)
    {
        var response = await http.PostAsJsonAsync("api/note-folders", dto);
        return response.IsSuccessStatusCode
            ? await response.Content.ReadFromJsonAsync<NoteFolderResponseDto>()
            : null;
    }

    public async Task<NoteFolderResponseDto?> RenameNoteFolderAsync(int id, string name)
    {
        var response = await http.PutAsJsonAsync($"api/note-folders/{id}/name", new NoteFolderRenameDto(name));
        return response.IsSuccessStatusCode
            ? await response.Content.ReadFromJsonAsync<NoteFolderResponseDto>()
            : null;
    }

    // null = auf die oberste Ebene. Liefert null, wenn das Ziel ein Nachfahre wäre.
    public async Task<NoteFolderResponseDto?> MoveNoteFolderAsync(int id, int? parentFolderId)
    {
        var response = await http.PutAsJsonAsync($"api/note-folders/{id}/parent", new NoteFolderMoveDto(parentFolderId));
        return response.IsSuccessStatusCode
            ? await response.Content.ReadFromJsonAsync<NoteFolderResponseDto>()
            : null;
    }

    public async Task<bool> DeleteNoteFolderAsync(int id) =>
        (await http.DeleteAsync($"api/note-folders/{id}")).IsSuccessStatusCode;

    public async Task<bool> RestoreNoteFolderAsync(int id) =>
        (await http.PostAsync($"api/note-folders/{id}/restore", null)).IsSuccessStatusCode;

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

}
