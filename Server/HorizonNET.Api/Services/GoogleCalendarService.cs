using System.Net;
using Google;
using Google.Apis.Auth;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Auth.OAuth2.Flows;
using Google.Apis.Auth.OAuth2.Requests;
using Google.Apis.Auth.OAuth2.Responses;
using Google.Apis.Calendar.v3;
using Google.Apis.Calendar.v3.Data;
using Google.Apis.Services;
using HorizonNET.Api.Services.Calendar;
using HorizonNET.Domain.Entities;
using HorizonNET.Domain.Interfaces;
using HorizonNET.Shared.Transfer.DTOs;

namespace HorizonNET.Api.Services;

// Kapselt den OAuth-2.0-Flow und den Zugriff auf Google Calendar (Kalender-Anbieter
// "google", siehe ICalendarProvider). Persistiert wird nur der langlebige Refresh-Token;
// Access-Tokens beschafft die Google-Bibliothek bei Bedarf automatisch neu.
public class GoogleCalendarService : ICalendarProvider
{
    // Single-User-App: fester Schlüssel für die eine Verbindung.
    private const string UserKey = "user";

    private static readonly string[] Scopes =
    [
        CalendarService.Scope.CalendarEvents, // Termine lesen + schreiben
        "openid",
        "https://www.googleapis.com/auth/userinfo.email" // nur zum Anzeigen der verbundenen Adresse
    ];

    private readonly IGoogleConnectionRepository repo;
    private readonly ITaskRepository taskRepo;
    private readonly IConfiguration config;

    public GoogleCalendarService(
        IConfiguration config,
        IGoogleConnectionRepository repo,
        ITaskRepository taskRepo)
    {
        this.config = config;
        this.repo = repo;
        this.taskRepo = taskRepo;
    }

    public string Key => CalendarProviders.Google;

    // Ob Google-Credentials hinterlegt sind. Ohne sie bietet die Einstellungsseite
    // Google gar nicht erst an – wer nur Nextcloud nutzt, braucht keine.
    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(config["Google:ClientId"]) &&
        !string.IsNullOrWhiteSpace(config["Google:ClientSecret"]);

    // Erst hier (und nicht im Konstruktor) auf die Konfiguration bestehen: Der Service
    // wird über CalendarSyncService bei jedem Task-Request erzeugt, auch wenn gar nicht
    // Google, sondern Nextcloud verbunden ist.
    private GoogleAuthorizationCodeFlow CreateFlow()
    {
        var clientId = config["Google:ClientId"];
        var clientSecret = config["Google:ClientSecret"];
        if (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(clientSecret))
            throw new InvalidOperationException("Google:ClientId/ClientSecret ist nicht konfiguriert.");

        return new(new GoogleAuthorizationCodeFlow.Initializer
        {
            ClientSecrets = new ClientSecrets { ClientId = clientId, ClientSecret = clientSecret },
            Scopes = Scopes
        });
    }

    // Baut die URL zur Google-Zustimmungsseite. access_type=offline + prompt=consent
    // stellen sicher, dass Google (auch bei erneuter Verbindung) einen Refresh-Token liefert.
    public string BuildAuthorizationUrl(string redirectUri)
    {
        var request = (GoogleAuthorizationCodeRequestUrl)
            CreateFlow().CreateAuthorizationCodeRequest(redirectUri);
        request.AccessType = "offline";
        request.Prompt = "consent";
        return request.Build().ToString();
    }

    // Tauscht den Autorisierungscode gegen Tokens, ermittelt die E-Mail und speichert die Verbindung.
    public async Task HandleCallbackAsync(string code, string redirectUri)
    {
        var token = await CreateFlow()
            .ExchangeCodeForTokenAsync(UserKey, code, redirectUri, CancellationToken.None);

        // Bei wiederholter Zustimmung kann der Refresh-Token fehlen – dann den bestehenden behalten.
        var refreshToken = token.RefreshToken;
        if (string.IsNullOrEmpty(refreshToken))
            refreshToken = (await repo.GetAsync())?.RefreshToken;

        if (string.IsNullOrEmpty(refreshToken))
            throw new InvalidOperationException(
                "Kein Refresh-Token von Google erhalten. Bitte erneut verbinden und den Zugriff bestätigen.");

        // Googles granulare Zustimmung erlaubt es, einzelne Berechtigungen abzuwählen.
        // Ohne den Kalender-Scope käme später bei jedem Kalender-Zugriff ein 403
        // "insufficient scopes" – deshalb schon hier ablehnen, statt eine halbgare
        // Verbindung zu speichern.
        if (!string.IsNullOrEmpty(token.Scope) && !GrantedIncludesCalendar(token.Scope))
        {
            await repo.DeleteAsync();
            throw new GoogleScopeNotGrantedException();
        }

        string? email = null;
        if (!string.IsNullOrEmpty(token.IdToken))
            email = (await GoogleJsonWebSignature.ValidateAsync(token.IdToken)).Email;

        await repo.SaveAsync(new GoogleConnection
        {
            RefreshToken = refreshToken,
            Email = email,
            ConnectedAtUtc = DateTime.UtcNow
        });
    }

    public async Task<bool> IsConnectedAsync() => await repo.GetAsync() is not null;

    // E-Mail des verbundenen Kontos; null, wenn nicht verbunden oder unbekannt.
    public async Task<string?> GetAccountAsync() => (await repo.GetAsync())?.Email;

    public Task DisconnectAsync() => repo.DeleteAsync();

    // Liefert eine autorisierte Credential für API-Aufrufe; refresht den Access-Token
    // bei Bedarf automatisch aus dem Refresh-Token. Null, wenn nicht verbunden.
    public async Task<UserCredential?> GetCredentialAsync()
    {
        var conn = await repo.GetAsync();
        if (conn is null) return null;

        var token = new TokenResponse { RefreshToken = conn.RefreshToken };
        return new UserCredential(CreateFlow(), UserKey, token);
    }

    // Prüft, ob die erteilten Scopes den Kalender-Schreib-/Lese-Scope enthalten.
    private static bool GrantedIncludesCalendar(string grantedScopes) =>
        grantedScopes.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                     .Contains(CalendarService.Scope.CalendarEvents);

    // Erkennt einen von Google widerrufenen oder abgelaufenen Refresh-Token.
    // Tritt im Testing-Modus zwangsläufig nach 7 Tagen auf.
    private static bool IsRevokedToken(TokenResponseException ex) =>
        ex.Error?.Error == "invalid_grant";

    // Erkennt einen 403, weil die Kalender-Berechtigung fehlt (Scope nicht erteilt).
    private static bool IsInsufficientScopes(GoogleApiException ex) =>
        ex.HttpStatusCode == HttpStatusCode.Forbidden &&
        (ex.Error?.Errors?.Any(e =>
             e.Reason is "ACCESS_TOKEN_SCOPE_INSUFFICIENT" or "insufficientPermissions") == true
         || ex.Message.Contains("insufficient authentication scopes", StringComparison.OrdinalIgnoreCase));

    // Verwirft die gespeicherte Verbindung, wenn Google sie nicht mehr akzeptiert
    // (Refresh-Token widerrufen/abgelaufen ODER Kalender-Scope fehlt). Danach zeigt die
    // App wieder "nicht verbunden" und signalisiert dem Nutzer, dass er neu verbinden muss.
    private Task InvalidateConnectionAsync() => repo.DeleteAsync();

    // Liest die Termine des primären Kalenders im angegebenen (UTC-)Zeitraum.
    // Leere Liste, wenn nicht verbunden.
    public async Task<IReadOnlyList<CalendarEventDto>> GetEventsAsync(
        DateTime fromUtc, DateTime toUtc, IReadOnlySet<string> ownIds)
    {
        var credential = await GetCredentialAsync();
        if (credential is null) return [];

        try
        {
            using var service = CreateCalendarService(credential);

            var request = service.Events.List("primary");
            request.TimeMinDateTimeOffset = new DateTimeOffset(fromUtc, TimeSpan.Zero);
            request.TimeMaxDateTimeOffset = new DateTimeOffset(toUtc, TimeSpan.Zero);
            request.SingleEvents = true; // wiederkehrende Termine zu Einzelterminen expandieren
            request.OrderBy = EventsResource.ListRequest.OrderByEnum.StartTime;
            request.ShowDeleted = false;
            request.MaxResults = 2500;

            var response = await request.ExecuteAsync();

            // Termine ausblenden, die die App selbst aus Tasks gespiegelt hat – sonst
            // erschiene jeder geplante Task doppelt (lokaler Task + sein Google-Event).
            return (response.Items ?? [])
                .Where(e => e.Id is null || !ownIds.Contains(e.Id))
                .Select(ToEventDto)
                .Where(e => e is not null)
                .Select(e => e!)
                .ToList();
        }
        catch (TokenResponseException ex) when (IsRevokedToken(ex))
        {
            // Refresh-Token widerrufen/abgelaufen: Verbindung verwerfen und leer liefern,
            // statt den Kalender-Load serverseitig mit 500 scheitern zu lassen.
            await InvalidateConnectionAsync();
            return [];
        }
        catch (GoogleApiException ex) when (IsInsufficientScopes(ex))
        {
            // Kalender-Berechtigung fehlt: Verbindung verwerfen und leer liefern (statt 500).
            // Der Nutzer muss neu verbinden und den Kalenderzugriff bestätigen.
            await InvalidateConnectionAsync();
            return [];
        }
    }

    private static CalendarEventDto? ToEventDto(Event e)
    {
        if (e.Start is null) return null;

        // Termin mit Uhrzeit (DateTimeDateTimeOffset gesetzt) vs. ganztägig (nur Date).
        if (e.Start.DateTimeDateTimeOffset is { } startDto)
        {
            var endDto = e.End?.DateTimeDateTimeOffset ?? startDto;
            return new CalendarEventDto(
                e.Id, e.Summary ?? "(ohne Titel)",
                startDto.LocalDateTime, endDto.LocalDateTime, AllDay: false);
        }

        if (DateTime.TryParse(e.Start.Date, out var startDate))
        {
            // Bei ganztägigen Terminen ist End.Date exklusiv (Folgetag).
            var endDate = DateTime.TryParse(e.End?.Date, out var ed) ? ed : startDate.AddDays(1);
            return new CalendarEventDto(
                e.Id, e.Summary ?? "(ohne Titel)", startDate, endDate, AllDay: true);
        }

        return null;
    }

    private static CalendarService CreateCalendarService(UserCredential credential) =>
        new(new BaseClientService.Initializer
        {
            HttpClientInitializer = credential,
            ApplicationName = "HorizonNET"
        });

    // ── Einweg-Sync (App → Google) ───────────────────────────────────────────

    // Spiegelt einen Task in den Google-Kalender. Macht nichts, wenn nicht verbunden.
    // Best-effort: Fehler werden geschluckt, damit ein Google-Problem das lokale Speichern
    // niemals blockiert.
    public async Task SyncTaskAsync(TaskItem task, int? defaultReminderMinutes)
    {
        try
        {
            var credential = await GetCredentialAsync();
            if (credential is null) return;

            using var service = CreateCalendarService(credential);

            // Ungeplant (kein Fälligkeitsdatum): ein evtl. vorhandenes Event entfernen.
            if (task.DueDate is null)
            {
                if (!string.IsNullOrEmpty(task.CalendarEventId))
                {
                    await TryDeleteAsync(service, task.CalendarEventId);
                    await taskRepo.SetCalendarEventIdAsync(task.Id, null);
                }
                return;
            }

            var body = BuildEvent(TaskEventData.From(task, defaultReminderMinutes));

            if (string.IsNullOrEmpty(task.CalendarEventId))
            {
                var created = await service.Events.Insert(body, "primary").ExecuteAsync();
                await taskRepo.SetCalendarEventIdAsync(task.Id, created.Id);
            }
            else
            {
                try
                {
                    await service.Events.Update(body, "primary", task.CalendarEventId).ExecuteAsync();
                }
                catch (GoogleApiException ex) when (
                    ex.HttpStatusCode is HttpStatusCode.NotFound or HttpStatusCode.Gone)
                {
                    // Event wurde extern gelöscht → neu anlegen und Verknüpfung aktualisieren.
                    var recreated = await service.Events.Insert(body, "primary").ExecuteAsync();
                    await taskRepo.SetCalendarEventIdAsync(task.Id, recreated.Id);
                }
            }
        }
        catch (TokenResponseException ex) when (IsRevokedToken(ex))
        {
            // Refresh-Token widerrufen/abgelaufen: Verbindung verwerfen, damit die App
            // wieder "nicht verbunden" zeigt. Lokale Speicherung bleibt unberührt.
            await InvalidateConnectionAsync();
        }
        catch (GoogleApiException ex) when (IsInsufficientScopes(ex))
        {
            // Kalender-Berechtigung fehlt: Verbindung verwerfen (Nutzer muss neu verbinden).
            await InvalidateConnectionAsync();
        }
        catch
        {
            // Google-Sync ist best-effort; die lokale Speicherung bleibt unberührt.
        }
    }

    // Löscht das zu einem (bereits lokal entfernten) Task gehörende Google-Event.
    public async Task DeleteEventAsync(string googleEventId)
    {
        try
        {
            var credential = await GetCredentialAsync();
            if (credential is null) return;

            using var service = CreateCalendarService(credential);
            await TryDeleteAsync(service, googleEventId);
        }
        catch (TokenResponseException ex) when (IsRevokedToken(ex))
        {
            await InvalidateConnectionAsync();
        }
        catch (GoogleApiException ex) when (IsInsufficientScopes(ex))
        {
            await InvalidateConnectionAsync();
        }
        catch
        {
            // best-effort
        }
    }

    private static async Task TryDeleteAsync(CalendarService service, string eventId)
    {
        try
        {
            await service.Events.Delete("primary", eventId).ExecuteAsync();
        }
        catch (GoogleApiException ex) when (
            ex.HttpStatusCode is HttpStatusCode.NotFound or HttpStatusCode.Gone)
        {
            // Event bereits weg – nichts zu tun.
        }
    }

    // Baut den Google-Event-Body. Mit Uhrzeit → Termin mit Uhrzeit, sonst ganztägig
    // (End-Datum exklusiv) – die Regeln dazu stehen in TaskEventData.
    private static Event BuildEvent(TaskEventData data)
    {
        var ev = new Event
        {
            Summary = data.Title,
            Description = data.Description,
            Transparency = data.Busy ? "opaque" : "transparent",
            Reminders = BuildReminders(data.ReminderMinutes)
        };

        if (data.AllDay)
        {
            ev.Start = new EventDateTime { Date = data.Start.ToString("yyyy-MM-dd") };
            ev.End = new EventDateTime { Date = data.End.ToString("yyyy-MM-dd") };
        }
        else
        {
            ev.Start = new EventDateTime { DateTimeDateTimeOffset = new DateTimeOffset(data.Start) };
            ev.End = new EventDateTime { DateTimeDateTimeOffset = new DateTimeOffset(data.End) };
        }

        return ev;
    }

    // UseDefault=false ist der entscheidende Teil: Nur damit hört Google auf, die
    // Standard-Erinnerung des Kalenders anzuwenden. Eine leere Override-Liste heißt dann
    // "gar keine Erinnerung", ein Eintrag setzt genau die gewünschte Vorlaufzeit.
    private static Event.RemindersData BuildReminders(int? minutes) => new()
    {
        UseDefault = false,
        Overrides = minutes is int m
            ? [new EventReminder { Method = "popup", Minutes = m }]
            : []
    };
}

// Wird beim Verbinden ausgelöst, wenn die Kalender-Berechtigung nicht erteilt wurde.
public class GoogleScopeNotGrantedException : Exception
{
    public GoogleScopeNotGrantedException()
        : base("Kalenderzugriff wurde nicht erteilt.") { }
}
