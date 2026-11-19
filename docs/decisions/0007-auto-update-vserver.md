# ADR 0007: Auto-Update auf dem vServer – eigener Updater als geplante Aufgabe, Paket am GitHub-Release, self-contained

- Status: vorgeschlagen
- Datum: 2026-11-19

## Kontext

Jedes Release heißt heute Handarbeit auf dem vServer: Paket lokal bauen, kopieren, sichern, bei
neuem Patch die ASP.NET Core Runtime heben, Dienst stoppen, `app\` tauschen, starten, prüfen (ADR
0005, `docs/deployment-vserver.md`). FerretSharp und PokeArena aktualisieren sich per Velopack.
Velopack ist aber für Desktop-Apps eines angemeldeten Benutzers gebaut. HorizonNET ist ein
Windows-Dienst unter einem Konto ohne Admin-Rechte; er kann seine eigenen Dateien nicht ersetzen
und sich nicht selbst neu starten.

Entscheidungen des Nutzers (2026-11-19):
- Die App **meldet nur**, installiert wird auf Klick.
- Veröffentlicht wird **self-contained**.
- Der **Vertrauensanker GitHub-Konto** wird wie in FerretSharp und PokeArena akzeptiert; eine
  Attestation-Prüfung kommt in den Backlog.

## Entscheidung

Vorschlag (nicht bestätigt), Details in `docs/konzept-auto-update.md`:

- **Paket am Release:** Ein Workflow `release.yml` baut bei `release: published` das
  Deployment-Paket. Es ist self-contained `win-x64` und enthält `app\`, `tools\`, `updater\`,
  Manifest und `.sha256`. Der Workflow hängt es an das GitHub-Release; der Job mit Schreibrecht führt
  keinen Code aus dem Repo aus.
- **Prüfung in der API** (`Update:Mode` = `Off` | `Notify` | `Install`): Prüfung beim Start und alle
  6 Stunden gegen `releases/latest` des öffentlichen Repos, ohne Token. Anzeige in den Einstellungen
  mit Release-Notes als Text.
- **Installation durch `Tools/HorizonNET.Updater`**, gestartet als geplante Aufgabe „HorizonNET
  Update“ unter SYSTEM. Das Dienstkonto darf genau diese Aufgabe starten, sonst nichts; der Start ist
  der ganze Auftrag, das Dienstkonto schreibt nichts, was der Updater liest. Der Updater installiert
  immer das neueste Release mit Paket aus dem fest eingebauten Repo. Eine zweite Aufgabe „HorizonNET
  Recover“ rollt beim Systemstart ein unterbrochenes Update zurück; sie kann das Dienstkonto nicht
  starten.
- **Ablauf:** `prepare` (installierte Version) lädt, prüft und entpackt das Paket und übergibt an
  `apply` aus dem neuen Paket. `apply` stoppt den Dienst, sichert DB und Schlüsselring, tauscht
  `app\`/`tools\`/`updater\` (der alte Stand bleibt als `*.previous`), startet den Dienst und prüft
  `/api/version`. Bei einem Fehler rollt es Programm **und DB** zurück. Ein Journal macht einen
  abgebrochenen Lauf wiederherstellbar; während des Tauschs steht der Dienst auf „Deaktiviert“.
- **Engere Ordnerrechte:** Das Dienstkonto darf Programmordner nur lesen und ausführen, Sicherungen
  gar nicht sehen. Was es beschreiben kann (`data\`, `keys\`), sichert und spielt der Updater nur
  dateiweise zurück, ohne Verweisen (Reparse Points) zu folgen. Vorschlag (nicht bestätigt): Ein
  schmales Zeitfenster zwischen Prüfung und Kopie (TOCTOU) bleibt und wird hingenommen; es ganz zu
  schließen bräuchte Dateizugriffe über Handles (Review 2026-11-19).
- Docker bleibt bei `git pull && docker compose up -d --build`, mit Hinweis auf neue Versionen.

## Konsequenzen

- ADR 0005 (self-contained, keine Runtime auf dem Server) und ADR 0006 (Paket am Release) haben je
  einen Nachtrag.
- Ein .NET-Sicherheitsupdate erreicht den Server nur über ein HorizonNET-Patch-Release. Das Paket
  bricht ab, wenn seine Runtime nicht zum Patch der Microsoft-Pakete passt.
- Wer das GitHub-Konto oder den Workflow übernimmt, kann Code auf den Server bringen, der beim
  nächsten Klick mit SYSTEM-Rechten installiert wird. Das ist akzeptiert; Attestation oder eine
  eigene Paketsignatur stehen im Backlog.
- Etwa 100 MB je Update, kein Delta; 30–120 s Ausfall je Update.
- Ein Rollback setzt die DB auf den Stand vor dem Update zurück.
- Releases vor dem Umstieg haben kein Paket und werden vom Updater ignoriert. Der Umstieg selbst ist
  einmal Handarbeit (Version einspielen, `server-install.ps1` erneut ausführen, Probe-Update).
- Verworfen:
  - **Velopack:** nicht für Dienste gebaut.
  - **Push aus GitHub Actions:** Ein Self-hosted Runner auf einem öffentlichen Repo oder eine
    Remote-Shell durch den Tunnel.
  - **Vollautomatisch nachts:** Migrationen laufen beim Start, und ein versehentlicher Tag ginge
    unbemerkt live.
  - **Updater im Dienst selbst:** Er bräuchte Admin-Rechte.
