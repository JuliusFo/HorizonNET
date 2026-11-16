# ADR 0003: Feldverschlüsselung per DataProtection statt SQLCipher

- Status: akzeptiert
- Datum: 2026-08-02 (Journal, Phase 14)

## Kontext

Mit dem Journal kamen die sensibelsten Daten der App. Geplant war ursprünglich eine Verschlüsselung in zwei Schichten: die ganze Datei per SQLCipher (A) und zusätzlich die Journal-Texte per Feldverschlüsselung (B).

Bei der Umsetzung zeigte sich: Alle SQLCipher-Pakete der SQLitePCLRaw-Familie enden bei 2.1.11. Genau diese Versionen betrifft CVE-2025-6965 (CVSS 9.8, Speicherkorruption in SQLite < 3.50.2); einen Patch gibt es nur in der 3.x-Linie für `e_sqlite3`. SQLCipher hätte bedeutet, eine bewusst geschlossene Hochrisiko-Lücke dauerhaft wieder zu öffnen.

## Entscheidung

- **Kein SQLCipher.** Der gepatchte Treiber `bundle_e_sqlite3` bleibt.
- **Feldverschlüsselung** über ASP.NET Core **DataProtection**, eingehängt als EF-`ValueConverter` (`EncryptedConverter`). Verschlüsselt sind Journal-Texte und -Titel, Stimmungsnotizen sowie alle Zugangsdaten zu Fremddiensten (Google-Refresh-Token, CalDAV-App-Passwort, Azure-DevOps-PAT).
- `Strict` für Spalten, die nie Klartext enthielten; `Lenient` für Spalten, die vor der Verschlüsselung schon Klartext hatten. Der Protector-Purpose je Spalte ist fest und ändert sich nie.
- Für die Platte **BitLocker**.
- Der Schlüsselring liegt außerhalb der DB (`DataProtection:KeyRingPath`); für den Umzug auf eine andere Maschine oder nach Linux gibt es `Tools/HorizonNET.KeyMigration`.

## Konsequenzen

- Bewusst akzeptiert: In einer kopierten `.db` bleiben Tasks, Notizen, Projekte, Termine und Sportdaten lesbar. BitLocker deckt „Platte gestohlen“ ab, nicht „Datei weitergegeben“.
- **Schlüsselring verloren = verschlüsselte Inhalte verloren.** DB und Schlüsselring gehören zusammen ins Backup, aber getrennt aufbewahrt.
- Suche über verschlüsselte Felder: SQL-Vorfilter, der Rest wird im Speicher entschlüsselt.
- Jede neue verschlüsselte Spalte muss zusätzlich im KeyMigration-Tool eingetragen werden.
- Eine spätere Ausweitung auf Notizen und Task-Beschreibungen ist möglich, kostet aber die SQL-Suche dort und eine Migration des Klartext-Bestands.
- Details und Abwägung: `docs/konzept-journal.md` (lokal).
