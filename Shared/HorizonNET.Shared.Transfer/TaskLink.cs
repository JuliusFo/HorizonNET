namespace HorizonNET.Shared.Transfer;

// Regel für das optionale Link-Feld eines Tasks: erlaubt sind ausschließlich
// http und https. Das schließt insbesondere javascript:-URLs aus, die beim Rendern
// als <a href> beim Klick ausgeführt würden.
//
// Bewusst geteilt: das Formular nutzt die Regel für die Rückmeldung, die API als
// verbindliche Prüfung – sonst hinge die Zusage am Wohlverhalten des Clients.
public static class TaskLink
{
    public static bool IsValid(string? url) =>
        !string.IsNullOrWhiteSpace(url)
        && Uri.TryCreate(url, UriKind.Absolute, out var uri)
        && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
}
