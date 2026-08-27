namespace HorizonNET.App.Services;

public record ConfirmRequest(string Title, string Message, string ConfirmLabel, bool Danger, string CancelLabel);

// Zentrale Bestätigungs-Dialoge. Aufrufer: `if (await Confirm.ShowAsync(...)) { ... }`.
// Ein einzelner ConfirmDialogHost (im MainLayout) rendert den Dialog und löst das
// zurückgegebene Task<bool> auf. Muster analog zum ToastService.
public class ConfirmService
{
    private TaskCompletionSource<bool>? _tcs;

    public ConfirmRequest? Current { get; private set; }

    public event Action? OnChange;

    // cancelLabel: Beschriftung des Nein-Wegs. Der Default passt für Bestätigungen
    // ("Abbrechen" = nichts passiert); Rückfragen mit zwei echten Ausgängen (z. B.
    // "Mit Sub-Tasks abschließen" / "Nur diesen Task") benennen ihn passend um.
    public Task<bool> ShowAsync(string title, string message, string confirmLabel = "Löschen", bool danger = true,
                                string cancelLabel = "Abbrechen")
    {
        // Laufende Anfrage abbrechen (sollte praktisch nicht vorkommen).
        _tcs?.TrySetResult(false);

        Current = new ConfirmRequest(title, message, confirmLabel, danger, cancelLabel);
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
