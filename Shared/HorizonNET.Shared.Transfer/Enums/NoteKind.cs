namespace HorizonNET.Shared.Transfer.Enums;

// Art einer Notiz. Diskriminator, der bestimmt, welcher Editor geöffnet wird und wie
// der Content zu lesen ist. Default 0 = Html → bestehende Notizen bleiben ohne
// Datenmigration korrekt.
public enum NoteKind
{
    Html = 0,
    Drawing = 1,
}
