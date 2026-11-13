Daily life handler including:
- Task-Planner
- Calendar
- Notes

Written with the help of claude code as a personal project.

## Docker

Die API liefert den Blazor-Client mit aus. Ein Container genügt also, die Daten (SQLite + Schlüsselring) liegen im Volume `/data`.

```bash
cp .env.example .env      # Benutzername, Startpasswort, Port usw. eintragen
docker compose up -d --build
```

Danach ist die App unter `http://<host>:8080` erreichbar (Port über `HORIZON_PORT`, z. B. `192.168.2.40:8095`, um nur an eine IP zu binden).

| Variable (`.env`)          | Bedeutung |
|----------------------------|-----------|
| `HORIZON_PORT`             | Host-Port, optional mit IP (`ip:port`) |
| `HORIZON_ALLOWED_HOSTS`    | Erlaubte Hostnamen, Semikolon-getrennt (`*` = alle) |
| `HORIZON_USERNAME` / `HORIZON_INITIAL_PASSWORD` | Konto, das beim allerersten Start angelegt wird |
| `HORIZON_SECURE_COOKIE`    | `true` = Login nur über HTTPS (Reverse Proxy). `false` = auch über reines `http://` im Heimnetz |
| `HORIZON_TRUSTED_PROXIES`  | CIDR des Reverse Proxys, dessen `X-Forwarded-*`-Header gelten (z. B. `172.16.0.0/12` für Docker-Netze) |
| `GOOGLE_CLIENT_ID` / `GOOGLE_CLIENT_SECRET` | Google-Kalender (optional; für Nextcloud nicht nötig) |
| `TZ`                       | Zeitzone für Tagesgrenzen und Terminserien (Standard `Europe/Berlin`) |

**Hinter einem Reverse Proxy (nginx Proxy Manager, Traefik, Caddy …):** HTTPS am Proxy terminieren, auf den Container-Port weiterleiten, `HORIZON_SECURE_COOKIE=true` lassen und `HORIZON_TRUSTED_PROXIES` auf das Netz des Proxys setzen. Sonst sieht die App die Anfragen als `http`, und die Google-Redirect-URIs stimmen nicht.

**Kalender-Synchronisation:** Unter *Einstellungen* wird entweder Google **oder** Nextcloud verbunden – nie beide. Nextcloud braucht keine Umgebungsvariablen: Server-URL, Benutzername und ein App-Passwort (Nextcloud → *Einstellungen → Sicherheit → Geräte & Sitzungen*) eingeben, *Kalender suchen*, Kalender wählen, *Verbinden*. Beim Verbinden bzw. Wechseln werden alle geplanten Tasks ab heute im Hintergrund in den neuen Kalender geschrieben; Termine im alten Kalender bleiben stehen. Ohne Google-Credentials bietet die Seite nur Nextcloud an.

**Backup:** Das Volume `horizonnet-data` enthält `horizonnet.db` (+ `-wal`/`-shm`) **und** `keys/`. Beides gehört zusammen gesichert, denn ohne den Schlüsselring sind die verschlüsselten Inhalte (Journal …) verloren. Unter Linux liegt der Schlüsselring unverschlüsselt im Volume (kein DPAPI). Der Zugriff aufs Volume ist also gleichbedeutend mit Zugriff auf die Daten.

```bash
docker run --rm -v horizonnet_horizonnet-data:/data -v "$PWD":/backup alpine \
  tar czf /backup/horizonnet-$(date +%F).tar.gz -C /data .
```

**Update:** `git pull && docker compose up -d --build`. Migrationen laufen beim Start automatisch.

**Umzug einer bestehenden Windows-Installation:** Der DPAPI-geschützte Schlüsselring ist unter Linux nicht lesbar. Auf Windows mit `HorizonNET.KeyMigration reprotect` (ohne `--target-dpapi`) einen Transport-Ring erzeugen, dann DB + Transport-Ring ins Volume kopieren (`/data/horizonnet.db`, `/data/keys/`) und im Container prüfen:

```bash
docker compose exec horizonnet dotnet /app/tools/HorizonNET.KeyMigration.dll verify --db /data/horizonnet.db --keys /data/keys
```
