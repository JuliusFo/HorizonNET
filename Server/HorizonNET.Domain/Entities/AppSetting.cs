namespace HorizonNET.Domain.Entities;

// Serverseitige Einstellung als Schlüssel/Wert.
//
// Alle bisherigen Einstellungen liegen im localStorage des Clients – das reicht nicht,
// sobald der SERVER den Wert braucht. Erster Fall: die Vorlaufzeit der Erinnerung am
// gespiegelten Google-Termin, die beim Sync gesetzt wird und den Client gar nicht sieht.
//
// Bewusst generisch statt einer Spalte je Einstellung: Der nächste serverseitige Wert
// braucht dann keine Migration mehr.
public class AppSetting
{
    // Sprechender Schlüssel, z. B. "google.reminderMinutes".
    public string Key { get; set; } = string.Empty;

    // Immer als Zeichenkette abgelegt; die Auslegung gehört zum jeweiligen Aufrufer.
    public string Value { get; set; } = string.Empty;
}
