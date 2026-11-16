# Architekturentscheidungen (ADRs)

Eine Datei je Entscheidung, die man später nicht aus dem Code allein versteht: Stack, Hosting, Datenhaltung, Sicherheit, Schnittstellen nach außen. Kleine Entscheidungen bleiben Kommentare im Code.

Dieser Ordner ist versioniert; der Rest von `docs/` (Konzepte, Planung) bleibt lokal (`.gitignore`).

| Nr. | Entscheidung | Status |
|---|---|---|
| [0001](0001-same-origin-hosting.md) | Die API liefert den Blazor-WASM-Client aus (Same-Origin) | akzeptiert |
| [0002](0002-sqlite-ef-core.md) | SQLite mit EF Core, WAL-Modus, Migrationen beim Start | akzeptiert |
| [0003](0003-feldverschluesselung-statt-sqlcipher.md) | Feldverschlüsselung per DataProtection statt SQLCipher | akzeptiert |
| [0004](0004-kalender-provider.md) | Kalender-Sync: Google oder CalDAV, nur in eine Richtung | akzeptiert |
| [0005](0005-betrieb-vserver.md) | Betrieb als Windows-Dienst hinter Cloudflare Tunnel, Docker als Alternative | akzeptiert |
| [0006](0006-versionierung-release.md) | Versionierung und Release: SemVer, CHANGELOG, Tags, `/release` | akzeptiert |

Die ADRs 0001–0006 sind am 2026-11-16 nachträglich aufgeschrieben worden; sie halten Entscheidungen fest, die vorher schon getroffen und umgesetzt waren.

## Neue ADR

- Nächste freie Nummer, Dateiname `NNNN-kurzer-titel.md`, Zeile in der Tabelle oben.
- **Entschieden ist nur, was der Nutzer entschieden hat.** Ein Vorschlag von Claude bekommt den Status „vorgeschlagen“, bis der Nutzer zustimmt.
- Eine überholte Entscheidung wird nicht gelöscht: Status „ersetzt durch NNNN“, die neue ADR verweist zurück. Nachträge unten in der ADR mit Datum.

Vorlage:

```markdown
# ADR NNNN: Titel

- Status: vorgeschlagen | akzeptiert | ersetzt durch NNNN
- Datum: JJJJ-MM-TT

## Kontext

Was ist das Problem, welche Randbedingungen gibt es?

## Entscheidung

Was wird gemacht – konkret genug, dass man Abweichungen im Code erkennt.

## Konsequenzen

Was folgt daraus, was wird bewusst in Kauf genommen, was ist verworfen und warum?
```
