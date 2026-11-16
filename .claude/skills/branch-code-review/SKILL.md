---
name: branch-code-review
description: Änderungen eines Branches (oder noch nicht gepushte Commits auf master) von einem separaten Reviewer-Agenten prüfen lassen, Befunde nach Schwere sortiert mit Lösungsvorschlag zeigen und die freigegebenen beheben. Verwenden, wenn der Nutzer ein Code-Review des Branches, des PRs oder „der Änderungen“ möchte.
argument-hint: "[Schwerpunkt oder Bereich, z. B. „Sicherheit CalDAV“ oder „v1.3.0..HEAD“]"
---
Mit dem Nutzer Deutsch sprechen.

## 1. Umfang bestimmen

- `git fetch origin master`. Aktueller Branch: `git branch --show-current` (leer = detached HEAD → kurz ablehnen und aufhören).
- **Eigener Branch:** Basis = `git merge-base origin/master HEAD`.
- **Auf `master`** (der Nutzer committet oft direkt): Basis = `origin/master`, also die noch nicht gepushten Commits.
- **Bereich in `$ARGUMENTS`** (etwa `v1.3.0..HEAD`): dieser Bereich hat Vorrang.
- Zum Umfang gehören immer auch nicht committete und neue Dateien (`git status --porcelain`). Ist nichts geändert: sagen und aufhören.
- Ist `origin/master` an der Basis vorbei, das erwähnen, nicht mergen.
- Für den Reviewer sammeln: geänderte Dateien (`git diff --stat <basis>`), die Dateien in `.claude/rules/`, deren `paths` zu den geänderten Dateien passen, passende ADRs in `docs/decisions/`, den Schwerpunkt aus `$ARGUMENTS`.

## 2. Reviewer starten

**Einen** separaten Agenten starten (`general-purpose`). Nie selbst in dieser Session reviewen: Das Review soll unabhängig vom Autor des Codes sein. Ihm die Fakten geben, nicht die eigene Meinung zum Code. Der Prompt nennt:

- Repo-Pfad, Basis-SHA, HEAD-SHA, und dass nicht committete Änderungen dazugehören.
- `CLAUDE.md`, `.claude/skills/branch-code-review/checklist.md`, die passenden Regeln und ADRs.
- Den Schwerpunkt aus `$ARGUMENTS` als zusätzliche Priorität, nicht als einziges Thema.
- Nur lesen: keine Änderungen, keine Commits, kein Checkout, Stash oder Reset. Er darf `dotnet build HorizonNET.slnx` und `dotnet test HorizonNET.slnx` ausführen (vorher dem Nutzer sagen: ein laufender Client aus Visual Studio muss danach neu starten).
- Der Sicherheitsdurchgang der Checkliste ist Pflicht: Für jede Quelle von außen, die die Änderung berührt, den Weg zu den Senken verfolgen und sie nennen, auch wenn nichts falsch ist.
- Den geänderten Code im Zusammenhang prüfen (Aufrufer, verwandter Code, berührter Zustand) und auch, was fehlt (Tests, CHANGELOG, Doku, die andere Stelle, die dasselbe tut).
- Probleme außerhalb der Änderung nur, wenn die Änderung sie erreichbar macht oder verschlimmert; sonst getrennt als „vorher schon da“.
- Antwort auf Deutsch, je Befund: Schwere (S/M/K laut Checkliste), Ort `pfad:zeile`, Befund mit konkretem Fehlerszenario (Eingabe/Zustand → falsches Ergebnis), Lösungsvorschlag, Sicherheit (sicher / Verdacht). Danach kurz „Ohne Befund geprüft“.

Auf sein Ergebnis warten. Nicht parallel selbst reviewen.

## 3. Nachprüfen

Jeden Befund selbst am Code prüfen, bevor er gezeigt wird: **nachgeprüft** (im Code bestätigt), **Verdacht** (plausibel, braucht Nachstellen) oder **verworfen** mit einer Zeile Begründung (falsch gelesen, schon behandelt, Entscheidung des Nutzers). Dubletten zusammenlegen.

## 4. Zeigen

Als normaler Text im Chat, sortiert S → M → K, durchnummeriert:

| # | Schwere | Ort | Befund | Lösung | Status |

Ort als klickbarer Link. Danach: „Vorher schon da“, „Verworfen“ (je eine Zeile), „Ohne Befund geprüft“ (kurz), dann Fragen, wo ein Fix eine Entscheidung braucht (UX, Verhaltensänderung). Am Ende fragen, welche Punkte behoben werden sollen (Vorschlag: alle). **Anhalten und auf die Freigabe warten.**

## 5. Freigegebene Punkte beheben

- In dieser Session, ohne weitere Agenten. Reihenfolge S → K.
- Ein **Verdacht** wird zuerst nachgestellt (Test). Lässt er sich nicht nachstellen, nichts ändern und festhalten, warum.
- Jeder Bugfix mit einem Test, der ohne ihn scheitert, wo machbar.
- `dotnet build HorizonNET.slnx` (warnungsfrei) und `dotnet test HorizonNET.slnx` grün; UI-Änderungen in der laufenden App geprüft oder als ungetestet benannt.
- Nicht committen – der Nutzer committet selbst, außer er bittet ausdrücklich darum. Dann kleine Commits mit `Fix:`/`Test:`/`Refactor:`.
- Zweiter Durchgang: denselben Reviewer-Agenten (`SendMessage`, er kennt die Befunde) nur die Fixes prüfen lassen: Deckt jeder Fix seinen Befund, hat ein Fix etwas anderes kaputt gemacht, ist der Test aussagekräftig? Neue Befunde wie in Schritt 3 prüfen; bleiben welche, wie in Schritt 4 zeigen und wieder warten.
- `CHANGELOG.md` nur anpassen, wenn sich ein sichtbarer Eintrag ändert oder ein sichtbarer Fehler behoben wurde.
- **Lehren:** Für jeden behobenen Befund fragen, ob eine Regel ihn schon bei der ersten Umsetzung verhindert hätte. Ist es eine Falle, die in diesem Bereich wiederkommt (kein einmaliger Ausrutscher), eine Zeile in die Datei in `.claude/rules/` eintragen, deren `paths` den Code abdecken, mit Quelle in Klammern (`Review 2026-MM-TT`). Eine neue Regeldatei nur mit gültigem `paths`-Kopf. Sonst nichts eintragen: Die Regeln laden in jede Session, die den Bereich anfasst, und müssen kurz bleiben.
- Zum Schluss eine kurze Zusammenfassung auf Deutsch (behoben, offen, was zu testen ist). Vor einem Push fragen.
