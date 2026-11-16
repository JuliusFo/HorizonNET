# ADR 0006: Versionierung und Release – SemVer, CHANGELOG, Tags, `/release`

- Status: akzeptiert
- Datum: 2026-11-16 (Version zentral seit 2026-07-15, SemVer seit dem Livegang 2026-08-27)

## Kontext

Bis zum Livegang zählte die Minor-Version die abgeschlossenen Phasen (0.9.0 … 0.16.0). Danach gab es SemVer, aber keine Tags, kein CHANGELOG und keine Releases auf GitHub; was eine Version enthielt, stand nur in einem Kommentar in `Directory.Build.props`.

## Entscheidung

- **Eine Versionsquelle:** `<Version>` in `Directory.Build.props`; alle Assemblies (API, Client, Data, Domain, Shared) tragen dieselbe Version.
- **SemVer seit dem Livegang:** Patch für Fixes und Sicherheitsupdates, Minor für neue oder sichtbar geänderte Funktionen, Major für einen Bruch bestehender Installationen. Nur interne Änderungen ergeben kein Release.
- **`CHANGELOG.md`** im Format Keep a Changelog, auf Deutsch; Einträge kommen beim Umsetzen unter `[Unreleased]`. Abschnitt „Deployment“ für Migrationen, Runtime und Einstellungen.
- **Tag `vX.Y.Z` und GitHub-Release** je Version, die Release-Notes sind der CHANGELOG-Abschnitt. Erstes Release: v1.3.0.
- Ein Release läuft über den Claude-Skill **`/release`** (prüft CI, schlägt die Version vor, schreibt Version und CHANGELOG, baut, testet und veröffentlicht nach Bestätigung).

## Konsequenzen

- Die Versionen vor 1.3.0 haben keine Tags; ihre Historie steht nachgetragen im CHANGELOG.
- Das Deployment auf den vServer bleibt ein manueller Schritt nach dem Release; `/release` listet ihn auf.
- An das GitHub-Release werden keine Dateien angehängt; das Deployment-Paket entsteht lokal.
