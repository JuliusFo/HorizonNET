---
paths:
  - "Server/HorizonNET.Server.Tests/**"
  - "Client/HorizonNET.Client.Tests/**"
---

# Tests

- **xUnit v2** auf VSTest (`Microsoft.NET.Test.Sdk`, `xunit.runner.visualstudio`). `dotnet test HorizonNET.slnx` im Root testet alles.
- **Server:** `TestDatabase` gibt je Test eine In-Memory-SQLite-Datenbank (echte Fremdschlüssel, Cascade und Query-Filter) und je Schritt (Seed / Act / Assert) einen frischen Kontext – keine Prüfung, die nur dank Change-Tracking grün ist. Der `EphemeralDataProtectionProvider` ist bewusst statisch (EF cachet das Modell samt Protector).
- Controller werden direkt instanziiert (kein TestServer, kein HTTP); getestet wird, was der Controller aus der Anfrage macht.
- **Client-Tests** prüfen reine Logik (`SportMath`, `NoteTreeBuilder`, `WeeklyMail` …), keine Komponenten.
- **Die CI läuft unter Linux mit UTC:** keine Annahmen über die lokale Zeitzone, die Kultur (Zahlen- und Datumsformate), `\` in Pfaden oder Groß-/Kleinschreibung von Dateinamen. Zeitabhängige Logik bekommt die Zeit hereingereicht, statt `DateTime.Now` zu lesen.
- Ein Bugfix kommt mit einem Test, der ohne ihn scheitert, wo machbar.
