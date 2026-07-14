namespace HorizonNET.App.Services;

public record ConfirmRequest(string Title, string Message, string ConfirmLabel, bool Danger);

// Zentrale Bestätigungs-Dialoge. Aufrufer: `if (await Confirm.ShowAsync(...)) { ... }`.
// Ein einzelner ConfirmDialogHost (im MainLayout) rendert den Dialog und löst das
// zurückgegebene Task<bool> auf. Muster analog zum ToastService.
public class ConfirmService
{
    private TaskCompletionSource<bool>? _tcs;

    public ConfirmRequest? Current { get; private set; }

    public event Action? OnChange;

    public Task<bool> ShowAsync(string title, string message, string confirmLabel = "Löschen", bool danger = true)
    {
        // Laufende Anfrage abbrechen (sollte praktisch nicht vorkommen).
        _tcs?.TrySetResult(false);

        Current = new ConfirmRequest(title, message, confirmLabel, danger);
        _tcs = new TaskCompletionSource<bool>();
        OnChange?.Invoke();
        return _tcs.Task;
    }

    public void Respond(bool confirmed)
    {
        Current = null;
        OnChange?.Invoke();
        _tcs?.TrySetResult(confirmed);
        _tcs = null;
    }
}
