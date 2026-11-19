---
name: release
description: HorizonNET-Release vorbereiten und veröffentlichen – Version, CHANGELOG, Tag, GitHub-Release, Hinweise fürs Deployment
disable-model-invocation: true
---

Ein Release von HorizonNET. Arbeitssprache Deutsch. Zwei Stellen brauchen das OK des Nutzers: die Versionsnummer (Schritt 2) und das Veröffentlichen (Schritt 5). Vorher nichts committen, pushen oder taggen.

1. **Ausgangslage prüfen.**
   - Auf `master`, Arbeitsbaum sauber (`git status`). Sonst anhalten und fragen.
   - `git fetch` und `git pull --ff-only`: Inzwischen gemergte PRs (etwa von Dependabot) sollen mit ins Release.
   - Die CI des aktuellen Commits muss grün sein: `gh run list --workflow ci.yml --commit <volle SHA>`. Läuft sie noch, abwarten; ist sie rot, anhalten.

2. **Inhalt sammeln und Version vorschlagen.**
   - Letzter Tag: `git describe --tags --abbrev=0`. Commits seitdem: `git log --oneline <tag>..HEAD`.
   - Mit dem Abschnitt `[Unreleased]` in `CHANGELOG.md` abgleichen. Fehlt ein sichtbarer Commit dort, einen Eintrag vorschlagen.
   - Hinweise fürs Deployment ermitteln:
     - Neue Migrationen: `git diff --name-only --diff-filter=A <tag>..HEAD -- Server/HorizonNET.Data/Migrations` (nur die `*_Name.cs`, nicht `.Designer.cs`). Jede neue Migration heißt: vorher DB sichern.
     - Microsoft-Pakete auf neuem Patch (`Directory.Packages.props`): `ASPNET_VERSION` im `Dockerfile` muss dazu passen. Der vServer bekommt die Runtime mit dem self-contained Paket.
     - Neue Einstellungen oder Umgebungsvariablen (`appsettings.json`, `.env.example`).
   - SemVer: nur **Intern** → kein Release nötig, dem Nutzer das sagen; **Behoben**/**Sicherheit** → Patch; **Neu** oder sichtbar **Geändert** → Minor; Bruch für bestehende Installationen → Major.
   - Zusammenfassung (Commits, geplante CHANGELOG-Abschnitte, Deployment-Hinweise) und die vorgeschlagene Nummer zeigen. **Auf das OK warten.**

3. **Version und CHANGELOG anpassen.**
   - `<Version>` in `Directory.Build.props` setzen. Nur diese Zeile.
   - `CHANGELOG.md`: `## [Unreleased]` wird `## [X.Y.Z] - JJJJ-MM-TT` (heutiges Datum), darüber ein neues leeres `## [Unreleased]`. Optional ein Satz unter der Überschrift, worum es im Release geht.
   - Abschnitte in der Reihenfolge Sicherheit, Neu, Geändert, Behoben, Intern, Deployment; leere weglassen. Den Abschnitt **Deployment** aus Schritt 2 füllen.
   - Links am Ende: `[Unreleased]` auf `compare/vX.Y.Z...HEAD`, neue Zeile `[X.Y.Z]: https://github.com/JuliusFo/HorizonNET/compare/v<vorher>...vX.Y.Z`.
   - Zeilenenden und Kodierung der Dateien beibehalten (kein BOM hinzufügen). `git diff --stat` zur Kontrolle.

4. **Bauen und testen.**
   - Vorher Bescheid geben: Der Build macht einen laufenden Client aus Visual Studio kaputt (Neustart nötig).
   - `dotnet build HorizonNET.slnx` (warnungsfrei, Warnungen sind Fehler) und `dotnet test HorizonNET.slnx --no-build`.
   - Deployment-Paket probeweise bauen: `scripts/deploy-package.ps1 -OutputDirectory <scratch>`. Das Skript prüft selbst, dass keine Entwicklungs-Konfiguration im Paket liegt und die Runtime zum Patch der Microsoft-Pakete passt. Ordner danach löschen.

5. **Veröffentlichen – erst nach OK.** Diff und Release-Notes (der neue CHANGELOG-Abschnitt) zeigen und fragen, ob veröffentlicht werden soll. Dann:
   - Commit `Chore: Release X.Y.Z` mit `Directory.Build.props` und `CHANGELOG.md`, Push auf `master`.
   - Auf die grüne CI dieses Commits warten (`gh run watch <id> --exit-status`). Rot → anhalten, nicht taggen.
   - Annotierter Tag: `git tag -a vX.Y.Z -m "HorizonNET X.Y.Z"`, `git push origin vX.Y.Z`.
   - GitHub-Release: den CHANGELOG-Abschnitt (ohne die Überschrift) in eine Datei im Scratchpad schreiben, dann `gh release create vX.Y.Z --verify-tag --title "HorizonNET X.Y.Z" --notes-file <datei>`. Keine Dateien von Hand anhängen.
   - **Auf das Paket warten:** Das Veröffentlichen startet `.github/workflows/release.yml`; der baut aus dem Tag `HorizonNET-X.Y.Z-win-x64.zip` samt `.sha256` und hängt beides an das Release. Lauf finden mit `gh run list --workflow release.yml --event release --commit <volle SHA> --json databaseId,status` (kann einige Sekunden dauern, bis er erscheint), dann `gh run watch <id> --exit-status`. Danach `gh release view vX.Y.Z --json assets --jq '.assets[].name'`: Beide Dateien müssen da sein. Ist der Lauf rot: anhalten, Log zeigen, das Release **nicht** löschen. Ein Fehler ohne Codeänderung (Netz, Runner) → `gh run rerun <id>`; braucht es eine Codeänderung, wird daraus ein neues Patch-Release. Ohne Paket findet der Updater auf dem vServer dieses Release nicht.

6. **Abschluss.** Link zum Release nennen und die Schritte auf dem vServer in Reihenfolge auflisten (siehe `docs/deployment-vserver.md`):
   1. DB und Schlüsselring sichern (`scripts/backup-database.ps1`) – Pflicht, wenn Migrationen dabei sind.
   2. Paket vom Release laden (`gh release download vX.Y.Z --pattern 'HorizonNET-*'`, Prüfsumme gegen die `.sha256` vergleichen), auf den Server kopieren, entpacken und dort `server-install.ps1 -SkipService` als Administrator ausführen (ersetzt `app\`, `tools\`, `updater\`, stoppt und startet den Dienst). Die Runtime steckt im Paket. Ist der Updater eingerichtet (ADR 0007), statt dessen in den Einstellungen „Über & Updates“ → „Jetzt aktualisieren“.
