namespace HorizonNET.App.Services;

public enum ToastLevel { Success, Error, Info }

// Eine einzelne Toast-Meldung. Action/ActionLabel sind optional und werden
// z. B. für „Rückgängig" (Undo, Paket 4c) genutzt.
public record ToastMessage(
    Guid Id,
    ToastLevel Level,
    string Text,
    string? ActionLabel,
    Func<Task>? Action);

// Zentrale Quelle für kurzlebige Benachrichtigungen (Toasts). Komponenten
// (ToastHost) abonnieren OnChange und rendern neu. Trägt sowohl das
// Netzwerkfehler-Feedback (4b) als auch später den Undo-Toast (4c).
public class ToastService
{
    private const int DefaultSuccessMs = 4000;
    private const int DefaultErrorMs   = 6000;

    private readonly List<ToastMessage> _toasts = [];

    public IReadOnlyList<ToastMessage> Toasts => _toasts;

    public event Action? OnChange;

    private const int DefaultUndoMs = 8000;

    public void ShowSuccess(string text) => Show(ToastLevel.Success, text, DefaultSuccessMs);
    public void ShowError(string text)   => Show(ToastLevel.Error,   text, DefaultErrorMs);
    public void ShowInfo(string text)    => Show(ToastLevel.Info,    text, DefaultSuccessMs);

    // Toast mit „Rückgängig"-Aktion (z. B. nach dem Löschen). Bleibt länger stehen.
    public void ShowUndo(string text, Func<Task> undo) =>
        Show(ToastLevel.Info, text, DefaultUndoMs, actionLabel: "Rückgängig", action: undo);

    // Toast mit „Neu laden"-Aktion bei erkanntem Versionsversatz (Phase 9c). Bleibt
    // ohne Auto-Dismiss stehen, bis der Nutzer neu lädt oder den Toast schließt.
    public void ShowUpdate(string text, Func<Task> reload) =>
        Show(ToastLevel.Info, text, durationMs: null, actionLabel: "Neu laden", action: reload);

    public ToastMessage Show(
        ToastLevel level,
        string text,
        int? durationMs = null,
        string? actionLabel = null,
        Func<Task>? action = null)
    {
        // Identische, noch sichtbare Meldung nicht doppelt anzeigen
        // (z. B. wenn beim Seitenaufruf mehrere Requests gleichzeitig scheitern).
        var existing = _toasts.FirstOrDefault(t =>
            t.Level == level && t.Text == text && t.Action is null && action is null);
        if (existing is not null)
            return existing;

        var toast = new ToastMessage(Guid.NewGuid(), level, text, actionLabel, action);
        _toasts.Add(toast);
        NotifyChanged();

        if (durationMs is int ms and > 0)
            _ = AutoDismissAsync(toast.Id, ms);

        return toast;
    }

    public void Remove(Guid id)
    {
        var idx = _toasts.FindIndex(t => t.Id == id);
        if (idx < 0) return;
        _toasts.RemoveAt(idx);
        NotifyChanged();
    }

    private async Task AutoDismissAsync(Guid id, int ms)
    {
        await Task.Delay(ms);
        Remove(id);
    }

    private void NotifyChanged() => OnChange?.Invoke();
}
