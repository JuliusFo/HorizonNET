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
| `HORIZON_ALLOWED_HOSTS`    | Erlaubte Hostnamen/IPs, Semikolon-getrennt. Standard `localhost;127.0.0.1` – für den Zugriff per Domain oder IP ergänzen |
| `HORIZON_USERNAME` / `HORIZON_INITIAL_PASSWORD` | Konto, das beim allerersten Start angelegt wird. Das Passwort danach aus `.env` löschen und `docker compose up -d` ausführen – sonst bleibt es per `docker inspect` lesbar |
| `HORIZON_SECURE_COOKIE`    | `true` = Login nur über HTTPS (Reverse Proxy). `false` = auch über reines `http://` im Heimnetz |
| `HORIZON_TRUSTED_PROXIES`  | IP bzw. CIDR des Reverse Proxys, dessen `X-Forwarded-*`-Header gelten – so eng wie möglich, siehe unten |
| `GOOGLE_CLIENT_ID` / `GOOGLE_CLIENT_SECRET` | Google-Kalender (optional; für Nextcloud nicht nötig) |
| `TZ`                       | Zeitzone für Tagesgrenzen und Terminserien (Standard `Europe/Berlin`) |

**Hinter einem Reverse Proxy (nginx Proxy Manager, Traefik, Caddy …):** HTTPS am Proxy terminieren, auf den Container-Port weiterleiten, `HORIZON_SECURE_COOKIE=true` lassen und `HORIZON_TRUSTED_PROXIES` setzen. Sonst sieht die App die Anfragen als `http`, und die Google-Redirect-URIs stimmen nicht.

Dabei gilt: Wem die App bei `X-Forwarded-For` vertraut, der bestimmt die Absender-IP – und damit das Login-Rate-Limit. Deshalb:

- Den Port **nicht** auf allen Interfaces veröffentlichen. Läuft der Proxy auf demselben Host, `HORIZON_PORT=127.0.0.1:8080`; läuft er als Container, den `ports:`-Eintrag entfernen und App und Proxy über ein gemeinsames Docker-Netz verbinden.
- In `HORIZON_TRUSTED_PROXIES` nur die konkrete Proxy-IP (z. B. `172.20.0.10/32`, feste IP im Compose-Netz) oder ein eigenes Netz eintragen, das nur App und Proxy teilen. **Nicht** pauschal `172.16.0.0/12`: Darin liegen alle Docker-Netze, und je nach Setup (userland-proxy, Docker Desktop, rootless) kommen auch direkte Zugriffe über die Gateway-IP `172.x.0.1` an.

**Kalender-Synchronisation:** Unter *Einstellungen* wird entweder Google **oder** Nextcloud verbunden – nie beide. Nextcloud braucht keine Umgebungsvariablen: Server-URL, Benutzername und ein App-Passwort (Nextcloud → *Einstellungen → Sicherheit → Geräte & Sitzungen*) eingeben, *Kalender suchen*, Kalender wählen, *Verbinden*. Beim Verbinden werden alle geplanten Tasks ab heute im Hintergrund in den Kalender geschrieben. Wer denselben Anbieter neu verbindet (etwa nach abgelaufenem Google-Token), bekommt seine bestehenden Termine aktualisiert, nicht verdoppelt. Beim Wechsel des Anbieters bleiben die Termine im alten Kalender stehen. Der Sync geht nur in eine Richtung: Änderungen an den Terminen im Kalender selbst überschreibt die App beim nächsten Speichern des Tasks. Ohne Google-Credentials bietet die Seite nur Nextcloud an.

**Backup:** Das Volume `horizonnet-data` enthält `horizonnet.db` (+ `-wal`/`-shm`) **und** `keys/`. Beides gehört zusammen gesichert, denn ohne den Schlüsselring sind die verschlüsselten Inhalte (Journal …) verloren. Unter Linux liegt der Schlüsselring unverschlüsselt im Volume (kein DPAPI). Der Zugriff aufs Volume ist also gleichbedeutend mit Zugriff auf die Daten.

Die Datenbank läuft im WAL-Modus. Eine Kopie der Dateien im laufenden Betrieb kann inkonsistent sein, deshalb den Container für das Backup kurz anhalten:

```bash
docker compose stop horizonnet
docker run --rm -v horizonnet_horizonnet-data:/data -v "$PWD":/backup alpine \
  tar czf /backup/horizonnet-$(date +%F).tar.gz -C /data .
docker compose start horizonnet
```

**Bind-Mount statt Volume:** Wer `./data:/data` einhängt, muss den Ordner vorher dem Container-Benutzer geben (`sudo chown -R 1654:1654 ./data`), sonst kann die App weder Datenbank noch Schlüsselring anlegen.

**Healthcheck:** Das Image prüft sich selbst (`/healthz`), `docker ps` zeigt `healthy`/`unhealthy`.

**Update:** `git pull && docker compose up -d --build`. Migrationen laufen beim Start automatisch.

**Umzug einer bestehenden Windows-Installation:** Der DPAPI-geschützte Schlüsselring ist unter Linux nicht lesbar. Auf Windows mit `HorizonNET.KeyMigration reprotect` (ohne `--target-dpapi`) einen Transport-Ring erzeugen, dann DB + Transport-Ring ins Volume kopieren (`/data/horizonnet.db`, `/data/keys/`) und im Container prüfen:

```bash
docker compose exec horizonnet dotnet /app/tools/HorizonNET.KeyMigration.dll verify --db /data/horizonnet.db --keys /data/keys
```
