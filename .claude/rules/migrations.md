---
paths:
  - "Server/HorizonNET.Data/Migrations/**"
  - "Server/HorizonNET.Data/AppDbContext.cs"
  - "Server/HorizonNET.Domain/Entities/**"
  - "scripts/*-migration.ps1"
---

# Migrationen und Datenmodell

- Anlegen nur über `scripts/add-migration.ps1 <Name>` (setzt Data als Projekt und Api als Startprojekt), Zurücknehmen über `scripts/remove-migration.ps1`.
- Migrationen laufen beim Start automatisch (`db.Database.Migrate()` in `Program.cs`) – auch auf dem vServer und in jedem Docker-Container. Eine kaputte Migration heißt: Die App startet nicht. Jede neue Migration gehört in den Abschnitt **Deployment** im `CHANGELOG.md` („vorher DB sichern“).
- `Down` sauber spiegeln. Umbenennen per `RenameColumn`/`RenameTable`, nie Drop + Add (Datenverlust). SQLite kann vieles nicht per `ALTER` – EF baut die Tabelle dann neu; die generierte Migration ansehen, bevor sie bleibt.
- **Daten-SQL gegen den echten Spaltentyp schreiben, nicht aus dem Enum schließen:** `TaskItem.Status` liegt als **Zahl** in der DB; nur einige Spalten (`Priority`, `Project.Status` …) haben `HasConversion<string>()` – in `AppDbContext` nachsehen. Ein `WHERE Status IN ('Done', …)` trifft nichts und scheitert lautlos (Journal 14h).
- Eindeutigkeit über einen Index absichern, nicht über Prüfen-dann-Anlegen; bei optionalen Spalten mit Filter (`… IS NOT NULL`). Bestehende Dubletten bereinigt die Migration vorher (PR #1, `AzureDevOpsWorkItemPerOrganization`).
- **Soft-Delete:** Entitäten mit `DeletedAt` bekommen einen Query-Filter; abhängige Entitäten filtern zusätzlich auf den Elternteil (`t.TaskItem!.DeletedAt == null`). Neue Entität mit `DeletedAt` → Filter, Papierkorb (`TrashController`) und Wiederherstellen mitdenken.
- **Verschlüsselte Spalten** (`EncryptedConverter`, ADR 0003): `Strict` für Inhalte, die nie Klartext waren; `Lenient` für Spalten, die vor der Verschlüsselung schon Klartext enthielten. Der Protector-Purpose (`"HorizonNET.…"`) ändert sich nie mehr, sonst ist der Bestand unlesbar. Jede neue verschlüsselte Spalte zusätzlich in `Tools/HorizonNET.KeyMigration/EncryptedColumns.cs` eintragen – sonst geht sie beim Schlüsselring-Umzug verloren (so beim Azure-DevOps-PAT bis 1.3.0). `KeyMigrationColumnsTests` prüft Spalten und Purposes gegen das Modell.
