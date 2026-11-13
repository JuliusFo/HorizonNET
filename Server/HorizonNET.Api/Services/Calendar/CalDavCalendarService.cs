using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Xml.Linq;
using HorizonNET.Domain.Entities;
using HorizonNET.Domain.Interfaces;
using HorizonNET.Shared.Transfer.DTOs;
using Ical.Net.CalendarComponents;
using Ical.Net.DataTypes;
using Ical.Net.Serialization;
using IcalCalendar = Ical.Net.Calendar;

namespace HorizonNET.Api.Services.Calendar;

// Verständliche Fehlermeldung für die Einstellungsseite (Verbinden / Kalender suchen).
public class CalDavException(string message) : Exception(message);

// Kalender-Anbieter "nextcloud": CalDAV (RFC 4791) mit Benutzer + App-Passwort per
// Basic-Auth. Getestet gegen Nextcloud (sabre/dav), sollte aber mit jedem CalDAV-Server
// funktionieren, der current-user-principal und calendar-home-set kennt.
//
// Jeder Task wird als eigene Ressource {Kalender-URL}{UID}.ics abgelegt. Die UID leitet
// sich aus dem Task ab (NewUid) – ein erneutes Spiegeln in denselben Kalender, etwa nach
// Trennen und Neuverbinden, überschreibt so den alten Termin statt ihn zu verdoppeln.
public class CalDavCalendarService(
    HttpClient http,
    ICalDavConnectionRepository repo,
    ITaskRepository taskRepo,
    ILogger<CalDavCalendarService> logger) : ICalendarProvider
{
    private static readonly XNamespace Dav = "DAV:";
    private static readonly XNamespace CalDav = "urn:ietf:params:xml:ns:caldav";

    private const int MaxRedirects = 5;

    private sealed record DavAuth(string Username, string Password);

    public string Key => CalendarProviders.Nextcloud;

    public async Task<bool> IsConnectedAsync() => await repo.GetAsync() is not null;

    public Task DisconnectAsync() => repo.DeleteAsync();

    public async Task<CalendarStatusDto> GetStatusAsync()
    {
        var c = await repo.GetAsync();
        return c is null
            ? new CalendarStatusDto(null, null, null)
            : new CalendarStatusDto(Key, c.Username, c.CalendarName);
    }

    // ── Verbinden ──────────────────────────────────────────────────────────────

    // Sucht die beschreibbaren Terminkalender des Kontos:
    // Server → current-user-principal → calendar-home-set → Kalender darin.
    public async Task<List<CalDavCalendarDto>> DiscoverAsync(string serverUrl, string username, string appPassword)
    {
        var server = NormalizeServerUrl(serverUrl)
            ?? throw new CalDavException("Ungültige Server-URL. Erwartet z. B. https://cloud.example.org");
        var auth = ValidateCredentials(username, appPassword);

        var principal = await FindPrincipalAsync(auth, server)
            ?? throw new CalDavException(
                "Unter dieser Adresse wurde kein CalDAV-Server gefunden. Stimmt die Server-URL?");

        var homeXml = await PropfindAsync(auth, principal, 0,
            "<c:calendar-home-set/>");
        var home = FindHref(homeXml.Xml, homeXml.Url, CalDav + "calendar-home-set")
            ?? throw new CalDavException("Der Server nennt keinen Kalender-Ordner für dieses Konto.");

        var listXml = await PropfindAsync(auth, home, 1,
            "<d:resourcetype/><d:displayname/><c:supported-calendar-component-set/><d:current-user-privilege-set/>");
        var calendars = ParseCalendars(listXml.Xml, listXml.Url);
        if (calendars.Count == 0)
            throw new CalDavException("Keine beschreibbaren Terminkalender gefunden.");

        return calendars;
    }

    // Prüft die Zugangsdaten, indem die Kalender erneut gesucht werden, und speichert nur,
    // wenn der gewählte Kalender wirklich zum Konto gehört. Das schützt auch davor, dass
    // eine beliebige vom Client geschickte URL mit den Zugangsdaten angesprochen wird.
    public async Task<CalendarStatusDto> ConnectAsync(CalDavConnectDto dto)
    {
        var calendars = await DiscoverAsync(dto.ServerUrl, dto.Username, dto.AppPassword);
        var chosen = calendars.FirstOrDefault(c => SameUrl(c.Url, dto.CalendarUrl))
            ?? throw new CalDavException("Der gewählte Kalender wurde nicht gefunden. Bitte erneut suchen.");

        var connection = new CalDavConnection
        {
            ServerUrl = NormalizeServerUrl(dto.ServerUrl)!.ToString().TrimEnd('/'),
            Username = dto.Username.Trim(),
            AppPassword = dto.AppPassword.Trim(),
            CalendarUrl = chosen.Url,
            CalendarName = chosen.Name,
            ConnectedAtUtc = DateTime.UtcNow
        };
        await repo.SaveAsync(connection);
        return new CalendarStatusDto(Key, connection.Username, connection.CalendarName);
    }

    // Nur http(s); ohne Schema wird https angenommen. Abschließender Slash wird ergänzt,
    // damit relative Pfade (remote.php/dav/) unter dem Server aufgelöst werden.
    public static Uri? NormalizeServerUrl(string? input)
    {
        if (string.IsNullOrWhiteSpace(input)) return null;
        var text = input.Trim();
        if (!text.Contains("://")) text = "https://" + text;
        if (!Uri.TryCreate(text, UriKind.Absolute, out var uri)) return null;
        if (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp) return null;

        var builder = new UriBuilder(uri) { Query = string.Empty, Fragment = string.Empty };
        if (!builder.Path.EndsWith('/')) builder.Path += "/";
        return builder.Uri;
    }

    private static DavAuth ValidateCredentials(string? username, string? password)
    {
        if (string.IsNullOrWhiteSpace(username)) throw new CalDavException("Bitte einen Benutzernamen angeben.");
        if (string.IsNullOrWhiteSpace(password)) throw new CalDavException("Bitte ein App-Passwort angeben.");
        return new DavAuth(username.Trim(), password.Trim());
    }

    // Probiert die üblichen Einstiegspunkte durch. Nextcloud antwortet unter
    // /remote.php/dav/; /.well-known/caldav leitet bei richtig eingerichtetem Webserver
    // dorthin um. Hat der Nutzer bereits eine DAV-URL eingefügt, wird sie direkt benutzt.
    private async Task<Uri?> FindPrincipalAsync(DavAuth auth, Uri server)
    {
        var candidates = server.AbsolutePath.Contains("/remote.php/", StringComparison.OrdinalIgnoreCase)
                         || server.AbsolutePath.Contains("/dav", StringComparison.OrdinalIgnoreCase)
            ? new[] { server }
            : [new Uri(server, "remote.php/dav/"), new Uri(server, ".well-known/caldav"), server];

        foreach (var candidate in candidates)
        {
            try
            {
                var result = await PropfindAsync(auth, candidate, 0, "<d:current-user-principal/>");
                var href = FindHref(result.Xml, result.Url, Dav + "current-user-principal");
                if (href is not null) return href;
            }
            catch (CalDavAuthException)
            {
                throw new CalDavException("Anmeldung fehlgeschlagen – Benutzername oder App-Passwort falsch.");
            }
            catch (CalDavException)
            {
                // nächster Kandidat
            }
        }
        return null;
    }

    // ── Sync (App → Kalender) ────────────────────────────────────────────────────

    public async Task SyncTaskAsync(TaskItem task, int? defaultReminderMinutes)
    {
        try
        {
            var conn = await repo.GetAsync();
            if (conn is null) return;

            // Ungeplant (kein Fälligkeitsdatum): ein evtl. vorhandenes Event entfernen.
            if (task.DueDate is null)
            {
                if (!string.IsNullOrEmpty(task.CalendarEventId))
                {
                    await DeleteResourceAsync(conn, task.CalendarEventId);
                    await taskRepo.SetCalendarEventIdAsync(task.Id, null);
                }
                return;
            }

            var uid = string.IsNullOrEmpty(task.CalendarEventId) ? NewUid(task) : task.CalendarEventId;
            var ics = BuildICalendar(uid, TaskEventData.From(task, defaultReminderMinutes), DateTime.UtcNow);

            using var response = await SendAsync(Auth(conn), HttpMethod.Put, EventUri(conn, uid),
                ics, "text/calendar; charset=utf-8");

            if (response.StatusCode == HttpStatusCode.Unauthorized)
            {
                await InvalidateConnectionAsync();
                return;
            }
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("CalDAV-PUT für Task {TaskId} fehlgeschlagen: {Status}",
                    task.Id, (int)response.StatusCode);
                return;
            }

            if (task.CalendarEventId != uid)
                await taskRepo.SetCalendarEventIdAsync(task.Id, uid);
        }
        catch (Exception ex)
        {
            // best-effort: Die lokale Speicherung bleibt unberührt.
            logger.LogWarning(ex, "CalDAV-Sync für Task {TaskId} fehlgeschlagen.", task.Id);
        }
    }

    public async Task DeleteEventAsync(string eventId)
    {
        try
        {
            var conn = await repo.GetAsync();
            if (conn is null) return;
            await DeleteResourceAsync(conn, eventId);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "CalDAV-Löschen von {EventId} fehlgeschlagen.", eventId);
        }
    }

    private async Task DeleteResourceAsync(CalDavConnection conn, string uid)
    {
        using var response = await SendAsync(Auth(conn), HttpMethod.Delete, EventUri(conn, uid));
        if (response.StatusCode == HttpStatusCode.Unauthorized)
            await InvalidateConnectionAsync();
        // 404/410: bereits weg – nichts zu tun.
    }

    // Stabile UID je Task: Id + Erstellzeitpunkt, damit eine nach dem endgültigen Löschen
    // wiederverwendete Id nicht den Termin eines früheren Tasks überschreibt.
    public static string NewUid(TaskItem task) =>
        $"horizonnet-task-{task.Id}-{task.CreatedAt.Ticks}";

    private static Uri EventUri(CalDavConnection conn, string uid) =>
        new(new Uri(conn.CalendarUrl), Uri.EscapeDataString(uid) + ".ics");

    // App-Passwort in Nextcloud widerrufen: Verbindung verwerfen, damit die App wieder
    // "nicht verbunden" zeigt (wie beim widerrufenen Google-Token).
    private Task InvalidateConnectionAsync() => repo.DeleteAsync();

    // Baut die .ics-Ressource. Termine mit Uhrzeit in UTC (Nextcloud zeigt sie in der
    // Zeitzone des Nutzers), ganztägige als DATE mit exklusivem Ende.
    public static string BuildICalendar(string uid, TaskEventData data, DateTime nowUtc)
    {
        var ev = new CalendarEvent
        {
            Uid = uid,
            Summary = data.Title,
            Description = string.IsNullOrEmpty(data.Description) ? null : data.Description,
            Transparency = data.Busy ? "OPAQUE" : "TRANSPARENT",
            DtStamp = new CalDateTime(DateTime.SpecifyKind(nowUtc, DateTimeKind.Utc), "UTC")
        };

        if (data.AllDay)
        {
            ev.DtStart = new CalDateTime(DateOnly.FromDateTime(data.Start));
            ev.DtEnd = new CalDateTime(DateOnly.FromDateTime(data.End));
        }
        else
        {
            ev.DtStart = new CalDateTime(data.Start.ToUniversalTime(), "UTC");
            ev.DtEnd = new CalDateTime(data.End.ToUniversalTime(), "UTC");
        }

        // Ohne VALARM erinnert der Kalender nicht – anders als bei Google gibt es bei
        // CalDAV keine still angewandte Standard-Erinnerung.
        if (data.ReminderMinutes is int minutes)
        {
            ev.Alarms.Add(new Alarm
            {
                Action = "DISPLAY",
                Description = data.Title,
                Trigger = new Trigger(minutes == 0 ? Duration.Zero : Duration.FromMinutes(-minutes))
            });
        }

        var calendar = new IcalCalendar { ProductId = "-//HorizonNET//HorizonNET//DE" };
        calendar.Events.Add(ev);
        return new CalendarSerializer().SerializeToString(calendar)!;
    }

    // ── Lesen ──────────────────────────────────────────────────────────────────

    public async Task<IReadOnlyList<CalendarEventDto>> GetEventsAsync(
        DateTime fromUtc, DateTime toUtc, IReadOnlySet<string> ownEventIds)
    {
        try
        {
            var conn = await repo.GetAsync();
            if (conn is null) return [];

            var start = fromUtc.ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture);
            var end = toUtc.ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture);

            // <c:expand> lässt den Server Serien zu Einzelterminen (in UTC) auflösen –
            // wie SingleEvents=true bei Google.
            var body = $"""
                <?xml version="1.0" encoding="utf-8"?>
                <c:calendar-query xmlns:d="DAV:" xmlns:c="urn:ietf:params:xml:ns:caldav">
                  <d:prop>
                    <c:calendar-data><c:expand start="{start}" end="{end}"/></c:calendar-data>
                  </d:prop>
                  <c:filter>
                    <c:comp-filter name="VCALENDAR">
                      <c:comp-filter name="VEVENT"><c:time-range start="{start}" end="{end}"/></c:comp-filter>
                    </c:comp-filter>
                  </c:filter>
                </c:calendar-query>
                """;

            using var response = await SendAsync(Auth(conn), new HttpMethod("REPORT"),
                new Uri(conn.CalendarUrl), body, "application/xml; charset=utf-8", depth: 1);

            if (response.StatusCode == HttpStatusCode.Unauthorized)
            {
                await InvalidateConnectionAsync();
                return [];
            }
            if (!response.IsSuccessStatusCode) return [];

            var xml = await response.Content.ReadAsStringAsync();
            return ParseEvents(xml, ownEventIds);
        }
        catch (Exception ex)
        {
            // Wie bei Google: lieber leer liefern, als den Kalender-Load scheitern zu lassen.
            logger.LogWarning(ex, "CalDAV-Termine konnten nicht geladen werden.");
            return [];
        }
    }

    // Liest die calendar-data-Blöcke einer REPORT-Antwort. Fremde Termine werden
    // zurückgegeben, die eigenen (ownEventIds = UIDs) und abgesagte ausgeblendet.
    public static List<CalendarEventDto> ParseEvents(string multistatusXml, IReadOnlySet<string> ownEventIds)
    {
        var result = new List<CalendarEventDto>();
        var doc = XDocument.Parse(multistatusXml);

        foreach (var data in doc.Descendants(CalDav + "calendar-data"))
        {
            IcalCalendar? cal;
            try { cal = IcalCalendar.Load(data.Value); }
            catch { continue; } // ein kaputter Termin soll nicht alle anderen verhindern

            if (cal is null) continue;
            foreach (var ev in cal.Events)
            {
                if (ev.Uid is not null && ownEventIds.Contains(ev.Uid)) continue;
                if (!ev.IsActive) continue;

                var dto = ToEventDto(ev);
                if (dto is not null) result.Add(dto);
            }
        }

        return result.OrderBy(e => e.Start).ToList();
    }

    private static CalendarEventDto? ToEventDto(CalendarEvent e)
    {
        if (e.DtStart is not { } dtStart) return null;
        var title = string.IsNullOrWhiteSpace(e.Summary) ? "(ohne Titel)" : e.Summary;

        DateTime start, end;
        bool allDay = !dtStart.HasTime;
        if (allDay)
        {
            start = dtStart.Date.ToDateTime(TimeOnly.MinValue);
            end = e.DtEnd is { } dtEnd
                ? dtEnd.Date.ToDateTime(TimeOnly.MinValue)
                : e.Duration is { } dur ? dtStart.Add(dur).Date.ToDateTime(TimeOnly.MinValue) : start.AddDays(1);
            if (end <= start) end = start.AddDays(1);
        }
        else
        {
            start = ToLocal(dtStart);
            end = e.DtEnd is { } dtEnd ? ToLocal(dtEnd)
                : e.Duration is { } dur ? ToLocal(dtStart.Add(dur))
                : start;
        }

        // Bei aufgelösten Serien teilen sich alle Vorkommen die UID – der Start macht die Id eindeutig.
        var id = $"{e.Uid}|{start:yyyyMMddTHHmm}";
        return new CalendarEventDto(id, title, start, end, allDay);
    }

    // Floating-Zeiten (ohne TZID) gelten als lokale Zeit; alles andere über UTC in die
    // Serverzeit – passend zu Google (DateTimeOffset.LocalDateTime).
    private static DateTime ToLocal(CalDateTime value)
    {
        if (value.IsFloating) return value.Value;
        try { return value.AsUtc.ToLocalTime(); }
        catch { return value.Value; } // unbekannte Zeitzone: Wandzeit statt gar nichts
    }

    // ── WebDAV-Grundlagen ──────────────────────────────────────────────────────────

    private sealed class CalDavAuthException() : CalDavException("Nicht angemeldet.");

    private sealed record DavResult(XDocument Xml, Uri Url);

    private static DavAuth Auth(CalDavConnection c) => new(c.Username, c.AppPassword);

    // PROPFIND mit den angegebenen Properties (Präfixe d: und c: sind deklariert).
    // Erwartet 207 Multi-Status; 401 wird als CalDavAuthException gemeldet.
    private async Task<DavResult> PropfindAsync(DavAuth auth, Uri url, int depth, string props)
    {
        var body = $"""
            <?xml version="1.0" encoding="utf-8"?>
            <d:propfind xmlns:d="DAV:" xmlns:c="urn:ietf:params:xml:ns:caldav">
              <d:prop>{props}</d:prop>
            </d:propfind>
            """;

        using var response = await SendAsync(auth, new HttpMethod("PROPFIND"), url, body,
            "application/xml; charset=utf-8", depth);

        if (response.StatusCode == HttpStatusCode.Unauthorized) throw new CalDavAuthException();
        if (response.StatusCode != HttpStatusCode.MultiStatus)
            throw new CalDavException($"Unerwartete Antwort vom Server ({(int)response.StatusCode}).");

        var text = await response.Content.ReadAsStringAsync();
        try
        {
            return new DavResult(XDocument.Parse(text), response.RequestMessage?.RequestUri ?? url);
        }
        catch (System.Xml.XmlException)
        {
            throw new CalDavException("Die Antwort des Servers ist kein gültiges WebDAV.");
        }
    }

    // Sendet mit Basic-Auth und folgt Umleitungen selbst: Der eingebaute Redirect von
    // HttpClient wirft den Authorization-Header weg (und machte aus PROPFIND u. U. GET).
    // Umleitungen auf einen anderen Host werden nicht verfolgt, damit das App-Passwort
    // nur an den Server geht, den der Nutzer eingetragen hat.
    private async Task<HttpResponseMessage> SendAsync(
        DavAuth auth, HttpMethod method, Uri url,
        string? body = null, string? contentType = null, int? depth = null)
    {
        var current = url;
        for (var i = 0; ; i++)
        {
            var request = new HttpRequestMessage(method, current);
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic",
                Convert.ToBase64String(Encoding.UTF8.GetBytes($"{auth.Username}:{auth.Password}")));
            if (depth is not null) request.Headers.Add("Depth", depth.Value.ToString(CultureInfo.InvariantCulture));
            if (body is not null)
            {
                request.Content = new StringContent(body, Encoding.UTF8);
                request.Content.Headers.ContentType = MediaTypeHeaderValue.Parse(contentType ?? "application/xml");
            }

            HttpResponseMessage response;
            try
            {
                response = await http.SendAsync(request);
            }
            catch (HttpRequestException)
            {
                request.Dispose();
                throw new CalDavException("Server nicht erreichbar. Stimmt die Server-URL?");
            }
            catch (TaskCanceledException)
            {
                request.Dispose();
                throw new CalDavException("Der Server hat nicht rechtzeitig geantwortet.");
            }

            var isRedirect = (int)response.StatusCode is 301 or 302 or 303 or 307 or 308;
            if (!isRedirect || response.Headers.Location is null || i >= MaxRedirects)
                return response;

            var next = response.Headers.Location.IsAbsoluteUri
                ? response.Headers.Location
                : new Uri(current, response.Headers.Location);
            response.Dispose();

            if (!string.Equals(next.Host, url.Host, StringComparison.OrdinalIgnoreCase))
                throw new CalDavException("Der Server leitet auf einen anderen Host um. Bitte dessen Adresse eintragen.");
            current = next;
        }
    }

    // Erstes <d:href> unterhalb der Property (z. B. current-user-principal), absolut gemacht.
    public static Uri? FindHref(XDocument xml, Uri baseUrl, XName property)
    {
        var href = xml.Descendants(property).Descendants(Dav + "href").FirstOrDefault()?.Value.Trim();
        return string.IsNullOrEmpty(href) ? null : new Uri(baseUrl, href);
    }

    // Kalender aus einer PROPFIND-Depth-1-Antwort auf das calendar-home-set: nur echte
    // Kalender-Collections mit VEVENT-Unterstützung (keine reinen Aufgabenlisten) und,
    // sofern der Server Rechte meldet, nur beschreibbare (keine abonnierten/geteilten
    // read-only-Kalender).
    public static List<CalDavCalendarDto> ParseCalendars(XDocument xml, Uri baseUrl)
    {
        var result = new List<CalDavCalendarDto>();

        foreach (var response in xml.Descendants(Dav + "response"))
        {
            var href = response.Element(Dav + "href")?.Value.Trim();
            if (string.IsNullOrEmpty(href)) continue;

            // Nur Properties aus erfolgreichen propstat-Blöcken (404-Blöcke listen fehlende).
            var props = response.Elements(Dav + "propstat")
                .Where(ps => ps.Element(Dav + "status")?.Value.Contains(" 200 ") == true)
                .Select(ps => ps.Element(Dav + "prop"))
                .Where(p => p is not null)
                .SelectMany(p => p!.Elements())
                .ToList();

            var resourceType = props.FirstOrDefault(p => p.Name == Dav + "resourcetype");
            if (resourceType?.Element(CalDav + "calendar") is null) continue;

            var components = props.FirstOrDefault(p => p.Name == CalDav + "supported-calendar-component-set");
            if (components is not null &&
                !components.Elements(CalDav + "comp").Any(c => (string?)c.Attribute("name") == "VEVENT"))
                continue;

            var privileges = props.FirstOrDefault(p => p.Name == Dav + "current-user-privilege-set");
            if (privileges is not null &&
                !privileges.Descendants(Dav + "privilege").Elements()
                    .Any(p => p.Name == Dav + "write" || p.Name == Dav + "write-content" || p.Name == Dav + "all"))
                continue;

            var url = new Uri(baseUrl, href).ToString();
            if (!url.EndsWith('/')) url += "/";

            var name = props.FirstOrDefault(p => p.Name == Dav + "displayname")?.Value.Trim();
            if (string.IsNullOrEmpty(name))
                name = Uri.UnescapeDataString(url.TrimEnd('/').Split('/').Last());

            result.Add(new CalDavCalendarDto(url, name));
        }

        return result.OrderBy(c => c.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    private static bool SameUrl(string a, string b) =>
        Uri.TryCreate(a, UriKind.Absolute, out var ua) && Uri.TryCreate(b, UriKind.Absolute, out var ub) &&
        Uri.Compare(ua, ub, UriComponents.HttpRequestUrl, UriFormat.Unescaped, StringComparison.OrdinalIgnoreCase) == 0;
}
