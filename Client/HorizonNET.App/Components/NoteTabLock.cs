using Microsoft.JSInterop;

namespace HorizonNET.App.Components;

// Der Tab-Lock einer Notiz, gemeinsam für Notiz- und Zeichnungs-Editor.
//
// Wozu: Zwei Tabs auf derselben Notiz haben sich gegenseitig den Stand überschrieben,
// weil jeder seinen eigenen, veralteten Inhalt speicherte. Deshalb darf nur ein Tab
// bearbeiten; alle weiteren zeigen schreibgeschützt. Die eigentliche Mechanik (localStorage,
// Heartbeat, BroadcastChannel) steckt in js/noteLock.js – hier liegt nur die C#-Seite.
//
// Kein DI-Dienst, sondern Zustand EINER Editor-Instanz: Wer den Lock hält, hängt an der
// gerade geöffneten Notiz. Die Komponente erzeugt ihn, hängt sich mit Lost ein und gibt
// ihn beim Aufräumen wieder frei.
public sealed class NoteTabLock(IJSRuntime js) : IAsyncDisposable
{
    private IJSObjectReference? module;
    private DotNetObjectReference<NoteTabLock>? selfRef;

    // Ob der Editor nur lesen darf – also ein anderer, lebender Tab die Notiz hält.
    public bool IsReadOnly { get; private set; }

    // Ein anderer Tab hat übernommen. Was dann zu tun ist, weiß nur die Komponente:
    // Der Notiz-Editor verwirft seinen Render-Merker, der Zeichnungs-Editor baut
    // zusätzlich die Zeichenfläche ab. IsReadOnly steht beim Aufruf bereits.
    public Func<Task>? Lost { get; set; }

    // Sperrt die Notiz für diesen Tab. Hält sie bereits ein anderer, lebender Tab, geht
    // der Editor in den Read-Only-Modus (force = true übernimmt trotzdem).
    public async Task AcquireAsync(int noteId, bool force)
    {
        module  ??= await js.InvokeAsync<IJSObjectReference>("import", "./js/noteLock.js");
        selfRef ??= DotNetObjectReference.Create(this);

        var acquired = await module.InvokeAsync<bool>("acquire", noteId, selfRef, force);
        IsReadOnly = !acquired;
    }

    // Gibt den gehaltenen Lock frei (Notiz abgewählt/gelöscht), damit ein anderer Tab sie
    // sofort bearbeiten kann statt erst nach Ablauf des TTL. noteId schützt beim Wechsel
    // zwischen den beiden Editoren davor, den frisch geholten Lock wieder zu entfernen.
    public async Task ReleaseAsync(int? noteId)
    {
        IsReadOnly = false;

        if (module is null) return;

        try
        {
            await module.InvokeVoidAsync("release", noteId);
        }
        catch (JSDisconnectedException) { }
    }

    // Von noteLock.js gerufen, wenn ein anderer Tab übernommen hat. Ausstehende Änderungen
    // werden bewusst verworfen statt gespeichert – genau dieses Zurückschreiben war das
    // Problem, gegen das der Lock existiert.
    [JSInvokable]
    public Task OnLockLost()
    {
        IsReadOnly = true;
        return Lost?.Invoke() ?? Task.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        if (module is not null)
        {
            // Beim Verlassen der Seite ist der JS-Kontext u. U. schon weg – der Lock läuft
            // dann über sein TTL ab, das ist unkritisch.
            try { await module.DisposeAsync(); }
            catch (JSDisconnectedException) { }
        }

        selfRef?.Dispose();
    }
}
