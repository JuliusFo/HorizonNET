namespace HorizonNET.Domain.Entities;

// Manuell angelegter Ordner für Notizen. Bewusst getrennt von der abgeleiteten Sicht
// (Projekt/Task): Eine Notiz kann in einem Ordner liegen UND an einem Projekt hängen –
// das sind zwei Sichten auf dieselbe Notiz, keine konkurrierenden Zuordnungen.
public class NoteFolder
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    // null = Ordner auf oberster Ebene. Beliebig tief verschachtelbar; beim Verschieben
    // verhindert das Repository Zyklen (ein Ordner darf nicht unter sich selbst landen).
    public int? ParentFolderId { get; set; }

    public NoteFolder? ParentFolder { get; set; }

    public ICollection<NoteFolder> Children { get; set; } = [];

    // Zeitstempel; ausschließlich serverseitig im Repository gesetzt.
    public DateTime CreatedAt { get; set; }

    // Soft-Delete: null = aktiv. Der Zeitstempel gruppiert einen Löschvorgang, damit Undo
    // genau die mitgelöschten Unterordner zurückholt (Muster wie bei Projekt/Task).
    //
    // Die Notizen IM Ordner werden bewusst NICHT mitgelöscht – eine Notiz ist ein Dokument,
    // ein Ordner nur Ablage. Ihre FolderId bleibt aber stehen: Solange der Ordner gelöscht
    // ist, behandelt die Oberfläche sie als „ohne Ordner"; wird er wiederhergestellt,
    // liegen sie wieder darin. Das ginge verloren, wenn wir die Zuordnung leerten.
    public DateTime? DeletedAt { get; set; }
}
