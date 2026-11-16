---
paths:
  - "Client/HorizonNET.App/**"
---

# Blazor-Client

- **Laufender Client:** Der Nutzer hat API und Client oft aus Visual Studio laufen. Ein `dotnet build` macht den laufenden Client kaputt (Runtime-Hash passt nicht mehr) – vorher Bescheid geben, danach neu starten lassen.
- **Auto-Save** (Notizen, Journal, Zeichnungen; Muster in `NoteEditor.razor`): Eingaben setzen `skipNextRender`, damit `ShouldRender` ein Render auslässt – sonst schreibt Radzen den Editor-DOM neu und verschluckt Text (1.0.3). Speichern entprellt (2 s); vor Wechsel oder Verlassen `FlushAsync`. Der Status („Gespeichert um …“) kommt aus dem `UpdatedAt` der Server-Antwort, nicht aus der Client-Uhr.
- **Veraltete Antworten verwerfen:** Wer bei schnellem Umschalten (Woche, Datum, Projekt) mehrere Anfragen auslösen kann, merkt sich die letzte und ignoriert ältere Antworten. Formular-Modelle sofort setzen und Nachgeladenes danach ergänzen, nie Eingaben mit einer späten Antwort überschreiben (Review PR #1, `ProjectEditDialog`).
- **SVG:** Zahlen mit `CultureInfo.InvariantCulture` formatieren (sonst Kommas aus der deutschen Kultur, Pfad ungültig). `<text>` ist in Razor ein Steuer-Tag und lässt sich nicht als SVG-Element schreiben – Beschriftungen als HTML darunter.
- Löschen immer mit Bestätigung (`ConfirmService`), Netzwerkfehler sichtbar (`ApiErrorHandler`, `ToastService`).
- `MarkupString` nur mit eigenem oder bereinigtem HTML; Links und `mailto:`-Adressen kodieren.
- Neue Datei in `wwwroot`: Sie steht erst nach einem Build im Static-Web-Assets-Manifest, vorher liefert der Server 401.
- **Layout-Check ohne Login:** statischer Mock mit demselben Markup in `wwwroot/`, bauen, Dev-Server neu starten, im Browser-Pane mit `resize_window` messen; danach Datei löschen und neu bauen. Passwörter tippt Claude nicht.
