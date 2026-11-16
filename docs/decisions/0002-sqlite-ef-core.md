# ADR 0002: SQLite mit EF Core, WAL-Modus, Migrationen beim Start

- Status: akzeptiert
- Datum: seit Projektbeginn; WAL-Modus seit 2026-08-11

## Kontext

HorizonNET hat genau einen Nutzer und läuft auf einem kleinen vServer oder in einem Container. Ein Datenbankserver wäre ein weiterer Dienst zum Betreiben, Sichern und Aktualisieren.

## Entscheidung

- **SQLite** als einzige Datenbank (`horizonnet.db`), Zugriff ausschließlich über **EF Core** (`AppDbContext`, Repositories in `HorizonNET.Data`). SQL entsteht parametrisiert über EF; Roh-SQL nur in Migrationen.
- **WAL-Modus** (`PRAGMA journal_mode=WAL` beim Start): Leser werden während eines Schreibvorgangs nicht ausgesperrt.
- **Migrationen laufen beim Start automatisch** (`db.Database.Migrate()`), auf dem vServer wie im Container. Kein separater Migrationsschritt im Deployment.
- **Soft-Delete** über `DeletedAt` mit globalen Query-Filtern; der Papierkorb liest an den Filtern vorbei.
- Der transitive SQLite-Treiber ist auf `SQLitePCLRaw.bundle_e_sqlite3` 3.x gehoben (CVE-2025-6965 in ≤ 2.1.11).

## Konsequenzen

- Backup = Datei kopieren, aber nicht im laufenden Betrieb (WAL: `-wal`/`-shm` gehören dazu); dafür `scripts/backup-database.ps1` bzw. Container stoppen. Der DataProtection-Schlüsselring gehört immer mit ins Backup (ADR 0003).
- Eine fehlerhafte Migration verhindert den Start. Deshalb vor jedem Deployment mit neuer Migration die DB sichern; der `CHANGELOG.md` nennt neue Migrationen im Abschnitt „Deployment“.
- SQLite-Grenzen: eingeschränktes `ALTER TABLE` (EF baut Tabellen neu), keine echte Parallelität beim Schreiben. Für einen Nutzer unkritisch.
- Tests nutzen In-Memory-SQLite statt des EF-InMemory-Providers, damit Fremdschlüssel, Cascade und Query-Filter wie in Produktion greifen.
