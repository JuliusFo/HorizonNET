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
using HorizonNET.Domain.Entities;
using HorizonNET.Domain.Interfaces;
using HorizonNET.Shared.Transfer.DTOs;

namespace HorizonNET.Api.Services;

// Kapselt den OAuth-2.0-Flow und (ab Phase 3) den Zugriff auf Google Calendar.
// Persistiert wird nur der langlebige Refresh-Token; Access-Tokens beschafft die
// Google-Bibliothek bei Bedarf automatisch neu.
public class GoogleCalendarService
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
    private readonly string clientId;
    private readonly string clientSecret;

    public GoogleCalendarService(IConfiguration config, IGoogleConnectionRepository repo, ITaskRepository taskRepo)
    {
        this.repo = repo;
        this.taskRepo = taskRepo;
        clientId = config["Google:ClientId"]
            ?? throw new InvalidOperationException("Google:ClientId ist nicht konfiguriert.");
        clientSecret = config["Google:ClientSecret"]
            ?? throw new InvalidOperationException("Google:ClientSecret ist nicht konfiguriert.");
    }

    private GoogleAuthorizationCodeFlow CreateFlow() =>
        new(new GoogleAuthorizationCodeFlow.Initializer
        {
            ClientSecrets = new ClientSecrets { ClientId = clientId, ClientSecret = clientSecret },
            Scopes = Scopes
        });

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

    public async Task<GoogleStatusDto> GetStatusAsync()
    {
        var conn = await repo.GetAsync();
        return new GoogleStatusDto(conn is not null, conn?.Email);
    }

    public Task<bool> DisconnectAsync() => repo.DeleteAsync();

    // Liefert eine autorisierte Credential für API-Aufrufe; refresht den Access-Token
    // bei Bedarf automatisch aus dem Refresh-Token. Null, wenn nicht verbunden.
    public async Task<UserCredential?> GetCredentialAsync()
    {
        var conn = await repo.GetAsync();
        if (conn is null) return null;

        var token = new TokenResponse { RefreshToken = conn.RefreshToken };
        return new UserCredential(CreateFlow(), UserKey, token);
    }

    // Liest die Termine des primären Kalenders im angegebenen (UTC-)Zeitraum.
    // Leere Liste, wenn nicht verbunden.
    public async Task<IReadOnlyList<GoogleEventDto>> GetEventsAsync(DateTime fromUtc, DateTime toUtc)
    {
        var credential = await GetCredentialAsync();
        if (credential is null) return [];

        using var service = CreateCalendarService(credential);

        var request = service.Events.List("primary");
        request.TimeMinDateTimeOffset = new DateTimeOffset(fromUtc, TimeSpan.Zero);
        request.TimeMaxDateTimeOffset = new DateTimeOffset(toUtc, TimeSpan.Zero);
        request.SingleEvents = true; // wiederkehrende Termine zu Einzelterminen expandieren
        request.OrderBy = EventsResource.ListRequest.OrderByEnum.StartTime;
        request.ShowDeleted = false;
        request.MaxResults = 2500;

        var response = await request.ExecuteAsync();

        return (response.Items ?? [])
            .Select(ToEventDto)
            .Where(e => e is not null)
            .Select(e => e!)
            .ToList();
    }

    private static GoogleEventDto? ToEventDto(Event e)
    {
        if (e.Start is null) return null;

        // Termin mit Uhrzeit (DateTimeDateTimeOffset gesetzt) vs. ganztägig (nur Date).
        if (e.Start.DateTimeDateTimeOffset is { } startDto)
        {
            var endDto = e.End?.DateTimeDateTimeOffset ?? startDto;
            return new GoogleEventDto(
                e.Id, e.Summary ?? "(ohne Titel)",
                startDto.LocalDateTime, endDto.LocalDateTime, AllDay: false);
        }

        if (DateTime.TryParse(e.Start.Date, out var startDate))
        {
            // Bei ganztägigen Terminen ist End.Date exklusiv (Folgetag).
            var endDate = DateTime.TryParse(e.End?.Date, out var ed) ? ed : startDate.AddDays(1);
            return new GoogleEventDto(
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
    public async Task SyncTaskAsync(TaskItem task)
    {
        try
        {
            var credential = await GetCredentialAsync();
            if (credential is null) return;

            using var service = CreateCalendarService(credential);

            // Ungeplant (kein Fälligkeitsdatum): ein evtl. vorhandenes Event entfernen.
            if (task.DueDate is null)
            {
                if (!string.IsNullOrEmpty(task.GoogleEventId))
                {
                    await TryDeleteAsync(service, task.GoogleEventId);
                    await taskRepo.SetGoogleEventIdAsync(task.Id, null);
                }
                return;
            }

            var body = BuildEvent(task);

            if (string.IsNullOrEmpty(task.GoogleEventId))
            {
                var created = await service.Events.Insert(body, "primary").ExecuteAsync();
                await taskRepo.SetGoogleEventIdAsync(task.Id, created.Id);
            }
            else
            {
                try
                {
                    await service.Events.Update(body, "primary", task.GoogleEventId).ExecuteAsync();
                }
                catch (GoogleApiException ex) when (
                    ex.HttpStatusCode is HttpStatusCode.NotFound or HttpStatusCode.Gone)
                {
                    // Event wurde extern gelöscht → neu anlegen und Verknüpfung aktualisieren.
                    var recreated = await service.Events.Insert(body, "primary").ExecuteAsync();
                    await taskRepo.SetGoogleEventIdAsync(task.Id, recreated.Id);
                }
            }
        }
        catch
        {
            // Google-Sync ist best-effort; die lokale Speicherung bleibt unberührt.
        }
    }

    // Löscht das zu einem (bereits lokal entfernten) Task gehörende Google-Event.
    public async Task DeleteTaskEventAsync(string googleEventId)
    {
        try
        {
            var credential = await GetCredentialAsync();
            if (credential is null) return;

            using var service = CreateCalendarService(credential);
            await TryDeleteAsync(service, googleEventId);
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

    // Baut den Google-Event-Body aus einem Task. Mit Start-/Endzeit → Termin mit Uhrzeit,
    // sonst ganztägig (End-Datum exklusiv) – spiegelt die Anzeige im Kalender.
    private static Event BuildEvent(TaskItem task)
    {
        var date = task.DueDate!.Value.Date;
        var ev = new Event { Summary = task.Title, Description = task.Description };

        if (task.StartTime is not null && task.EndTime is not null)
        {
            var start = date + task.StartTime.Value.TimeOfDay;
            var end = date + task.EndTime.Value.TimeOfDay;
            if (end <= start) end = start.AddHours(1);

            ev.Start = new EventDateTime { DateTimeDateTimeOffset = new DateTimeOffset(start) };
            ev.End = new EventDateTime { DateTimeDateTimeOffset = new DateTimeOffset(end) };
        }
        else
        {
            ev.Start = new EventDateTime { Date = date.ToString("yyyy-MM-dd") };
            ev.End = new EventDateTime { Date = date.AddDays(1).ToString("yyyy-MM-dd") };
        }

        return ev;
    }
}
