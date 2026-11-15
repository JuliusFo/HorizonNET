using System.Net;
using System.Text;
using Google.Apis.Calendar.v3.Data;
using HorizonNET.Api.Services;
using HorizonNET.Api.Services.Calendar;
using HorizonNET.Data;
using HorizonNET.Data.Repositories;
using HorizonNET.Domain.Entities;
using HorizonNET.Shared.Transfer.DTOs;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace HorizonNET.Server.Tests;

// Kalender-Fassade und CalDAV-Transport gegen einen nachgebauten Server (FakeHandler):
// Event-IDs beim Trennen/Neuverbinden, frische IDs unter der Sperre, und dass die
// Zugangsdaten nie an einen anderen Ursprung gehen.
public class CalendarSyncTests
{
    private const string Server = "https://cloud.example.org";
    private const string CalendarUrl = Server + "/remote.php/dav/calendars/max/personal/";

    // ── Event-IDs beim Trennen und Neuverbinden ─────────────────────────────────

    [Fact]
    public async Task Disconnect_KeepsEventIds()
    {
        using var db = new TestDatabase();
        await SeedTaskAsync(db, "uid-1");
        await SeedCalDavAsync(db);

        using (var ctx = db.NewContext())
            await TasksControllerTests.NewCalendarService(ctx).DisconnectAsync();

        Assert.Equal("uid-1", await EventIdAsync(db));
    }

    // Abgelaufener Google-Token oder bewusstes Trennen: Wer denselben Anbieter wieder
    // verbindet, bekommt seine Termine fortgeschrieben – nicht verdoppelt.
    [Fact]
    public async Task Reconnect_SameProvider_KeepsEventIds()
    {
        using var db = new TestDatabase();
        await SeedTaskAsync(db, "uid-1");
        await SeedCalDavAsync(db);

        using (var ctx = db.NewContext())
        {
            var calendar = TasksControllerTests.NewCalendarService(ctx);
            await new AppSettingRepository(ctx).SetAsync(CalendarSyncService.ProviderSettingKey, CalendarProviders.Nextcloud);
            await calendar.ConnectedAsync(NewCalDav(ctx));
        }

        Assert.Equal("uid-1", await EventIdAsync(db));
    }

    // Bestand aus der Zeit vor Nextcloud: Ohne gespeicherten Anbieter stammen die IDs
    // von Google – ein erneutes Google-Verbinden behält sie.
    [Fact]
    public async Task Reconnect_Google_WithoutStoredProvider_KeepsEventIds()
    {
        using var db = new TestDatabase();
        await SeedTaskAsync(db, "google-event-1");

        using (var ctx = db.NewContext())
        {
            var calendar = TasksControllerTests.NewCalendarService(ctx);
            var google = new GoogleCalendarService(new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build(),
                new GoogleConnectionRepository(ctx), new TaskRepository(ctx));
            await calendar.ConnectedAsync(google);
        }

        Assert.Equal("google-event-1", await EventIdAsync(db));
    }

    [Fact]
    public async Task Connect_OtherProvider_ClearsEventIds()
    {
        using var db = new TestDatabase();
        await SeedTaskAsync(db, "google-event-1");
        await SeedCalDavAsync(db);

        using (var ctx = db.NewContext())
            await TasksControllerTests.NewCalendarService(ctx).ConnectedAsync(NewCalDav(ctx));

        Assert.Null(await EventIdAsync(db));
        using var assert = db.NewContext();
        Assert.Equal(CalendarProviders.Nextcloud,
            await new AppSettingRepository(assert).GetAsync(CalendarSyncService.ProviderSettingKey));
    }

    // ── Frische Event-ID unter der Sperre ─────────────────────────────────────────

    // Der Request-Task trägt noch keine ID, der Hintergrund-Abgleich hat aber gerade eine
    // gespeichert. Gespiegelt wird auf die gespeicherte – kein zweiter Termin.
    [Fact]
    public async Task SyncTask_UsesEventIdFromDatabase_NotStaleEntity()
    {
        using var db = new TestDatabase();
        var id = await SeedTaskAsync(db, "uid-aus-db", due: DateTime.Today.AddDays(1));
        await SeedCalDavAsync(db);
        var requests = new List<HttpRequestMessage>();
        var handler = new FakeHandler(req => { requests.Add(req); return new HttpResponseMessage(HttpStatusCode.Created); });

        using (var ctx = db.NewContext())
        {
            var stale = await ctx.Tasks.AsNoTracking().SingleAsync(t => t.Id == id);
            stale.CalendarEventId = null;
            await TasksControllerTests.NewCalendarService(ctx, new HttpClient(handler)).SyncTaskAsync(stale);
        }

        var put = Assert.Single(requests);
        Assert.Equal(HttpMethod.Put, put.Method);
        Assert.EndsWith("/uid-aus-db.ics", put.RequestUri!.AbsolutePath);
        Assert.Equal("uid-aus-db", await EventIdAsync(db));
    }

    [Fact]
    public async Task Resync_MirrorsScheduledTasks_AndStopsWhenSuperseded()
    {
        using var db = new TestDatabase();
        await SeedTaskAsync(db, null, due: DateTime.Today.AddDays(1));
        await SeedCalDavAsync(db);
        var puts = 0;
        var handler = new FakeHandler(_ => { puts++; return new HttpResponseMessage(HttpStatusCode.Created); });
        var coordinator = new CalendarSyncCoordinator();

        using (var ctx = db.NewContext())
        {
            var calendar = TasksControllerTests.NewCalendarService(ctx, new HttpClient(handler), coordinator);

            var outdated = coordinator.NextResyncGeneration();
            coordinator.NextResyncGeneration();
            Assert.Equal(0, await calendar.ResyncAllAsync(outdated));
            Assert.Equal(0, puts);

            Assert.Equal(1, await calendar.ResyncAllAsync());
        }

        Assert.Equal(1, puts);
        Assert.StartsWith(CalDavCalendarService.OwnUidPrefix, await EventIdAsync(db));
    }

    // ── Eigene Termine erkennen ───────────────────────────────────────────────────

    // Auch ohne gespeicherte Event-ID (z. B. nach Google → Nextcloud → Google) sind die
    // eigenen Google-Termine an der Extended Property erkennbar.
    [Fact]
    public void Google_OwnEvents_AreRecognizedByMarker()
    {
        var task = new TaskItem { Id = 1, Title = "T", DueDate = new DateTime(2026, 11, 20) };
        var ev = GoogleCalendarService.BuildEvent(TaskEventData.From(task, null));
        ev.Id = "unbekannt";

        Assert.True(GoogleCalendarService.IsOwnEvent(ev, new HashSet<string>()));
        Assert.False(GoogleCalendarService.IsOwnEvent(new Event { Id = "fremd" }, new HashSet<string>()));
        Assert.True(GoogleCalendarService.IsOwnEvent(new Event { Id = "fremd" }, new HashSet<string> { "fremd" }));
    }

    // ── Zugangsdaten nur an den eingetragenen Ursprung ─────────────────────────────

    [Theory]
    [InlineData("http://cloud.example.org/remote.php/dav/")]      // Downgrade auf http
    [InlineData("https://evil.example.com/remote.php/dav/")]      // fremder Host
    [InlineData("https://cloud.example.org:8443/remote.php/dav/")] // anderer Port
    public async Task Discover_RedirectToOtherOrigin_IsNotFollowed(string location)
    {
        var requests = new List<Uri>();
        var handler = new FakeHandler(req =>
        {
            requests.Add(req.RequestUri!);
            var r = new HttpResponseMessage(HttpStatusCode.MovedPermanently);
            r.Headers.Location = new Uri(location);
            return r;
        });

        using var db = new TestDatabase();
        using var ctx = db.NewContext();
        await Assert.ThrowsAnyAsync<CalDavException>(
            () => NewCalDav(ctx, handler).DiscoverAsync(Server, "max", "pw"));

        Assert.NotEmpty(requests);
        Assert.All(requests, u => Assert.True(CalDavCalendarService.IsSameOrigin(u, new Uri(Server + "/")), u.ToString()));
    }

    // Wer http einträgt und auf https umgeleitet wird, bekommt einen klaren Hinweis.
    [Fact]
    public async Task Discover_HttpRedirectedToHttps_ExplainsWhatToEnter()
    {
        var handler = new FakeHandler(req =>
        {
            var r = new HttpResponseMessage(HttpStatusCode.MovedPermanently);
            r.Headers.Location = new UriBuilder(req.RequestUri!) { Scheme = "https", Port = 443 }.Uri;
            return r;
        });

        using var db = new TestDatabase();
        using var ctx = db.NewContext();
        var ex = await Assert.ThrowsAnyAsync<CalDavException>(
            () => NewCalDav(ctx, handler).DiscoverAsync("http://cloud.example.org", "max", "pw"));
        Assert.Contains("https://", ex.Message);
    }

    // Ein vom Server gelieferter absoluter href auf einen fremden Host wird nicht
    // angesprochen – und taugt nicht als Kalender-URL.
    [Fact]
    public async Task Discover_ForeignHrefs_AreNeitherRequestedNorOffered()
    {
        var requests = new List<Uri>();
        var handler = new FakeHandler(req =>
        {
            requests.Add(req.RequestUri!);
            var path = req.RequestUri!.AbsolutePath;
            return path switch
            {
                "/remote.php/dav/" => MultiStatus("""
                    <d:response><d:href>/remote.php/dav/</d:href><d:propstat><d:prop>
                      <d:current-user-principal><d:href>/remote.php/dav/principals/users/max/</d:href></d:current-user-principal>
                    </d:prop><d:status>HTTP/1.1 200 OK</d:status></d:propstat></d:response>
                    """),
                "/remote.php/dav/principals/users/max/" => MultiStatus("""
                    <d:response><d:href>/remote.php/dav/principals/users/max/</d:href><d:propstat><d:prop>
                      <c:calendar-home-set><d:href>/remote.php/dav/calendars/max/</d:href></c:calendar-home-set>
                    </d:prop><d:status>HTTP/1.1 200 OK</d:status></d:propstat></d:response>
                    """),
                "/remote.php/dav/calendars/max/" => MultiStatus("""
                    <d:response><d:href>/remote.php/dav/calendars/max/personal/</d:href><d:propstat><d:prop>
                      <d:resourcetype><d:collection/><c:calendar/></d:resourcetype><d:displayname>Persönlich</d:displayname>
                    </d:prop><d:status>HTTP/1.1 200 OK</d:status></d:propstat></d:response>
                    <d:response><d:href>https://evil.example.com/cal/</d:href><d:propstat><d:prop>
                      <d:resourcetype><d:collection/><c:calendar/></d:resourcetype><d:displayname>Falle</d:displayname>
                    </d:prop><d:status>HTTP/1.1 200 OK</d:status></d:propstat></d:response>
                    """),
                _ => new HttpResponseMessage(HttpStatusCode.NotFound)
            };
        });

        using var db = new TestDatabase();
        using var ctx = db.NewContext();
        var calendars = await NewCalDav(ctx, handler).DiscoverAsync(Server, "max", "pw");

        Assert.Equal(["Persönlich"], calendars.Select(c => c.Name));
        Assert.DoesNotContain(requests, u => u.Host == "evil.example.com");
    }

    [Fact]
    public async Task Discover_ForeignPrincipalHref_IsNotRequested()
    {
        var requests = new List<Uri>();
        var handler = new FakeHandler(req =>
        {
            requests.Add(req.RequestUri!);
            return MultiStatus("""
                <d:response><d:href>/remote.php/dav/</d:href><d:propstat><d:prop>
                  <d:current-user-principal><d:href>https://evil.example.com/principals/max/</d:href></d:current-user-principal>
                </d:prop><d:status>HTTP/1.1 200 OK</d:status></d:propstat></d:response>
                """);
        });

        using var db = new TestDatabase();
        using var ctx = db.NewContext();
        await Assert.ThrowsAnyAsync<CalDavException>(() => NewCalDav(ctx, handler).DiscoverAsync(Server, "max", "pw"));
        Assert.DoesNotContain(requests, u => u.Host == "evil.example.com");
    }

    // 303 See Other heißt: Ergebnis per GET (ohne Body) unter der neuen Adresse abholen.
    [Fact]
    public async Task SeeOther_SwitchesToGet()
    {
        var methods = new List<(HttpMethod Method, bool HasBody)>();
        var handler = new FakeHandler(req =>
        {
            methods.Add((req.Method, req.Content is not null));
            if (req.Method.Method == "PROPFIND")
            {
                var r = new HttpResponseMessage(HttpStatusCode.SeeOther);
                r.Headers.Location = new Uri("/anderswo", UriKind.Relative);
                return r;
            }
            return new HttpResponseMessage(HttpStatusCode.NotFound);
        });

        using var db = new TestDatabase();
        using var ctx = db.NewContext();
        await Assert.ThrowsAnyAsync<CalDavException>(
            () => NewCalDav(ctx, handler).DiscoverAsync(Server + "/remote.php/dav/", "max", "pw"));

        Assert.Equal([("PROPFIND", true), ("GET", false)], methods.Select(m => (m.Method.Method, m.HasBody)));
    }

    // Ein einzelner 401 (z. B. Nextclouds Brute-Force-Schutz) trennt die Verbindung nicht,
    // solange die Gegenprobe durchgeht.
    [Fact]
    public async Task TransientUnauthorized_KeepsConnection()
    {
        using var db = new TestDatabase();
        await SeedCalDavAsync(db);
        var handler = new FakeHandler(req => req.Method == HttpMethod.Put
            ? new HttpResponseMessage(HttpStatusCode.Unauthorized)
            : MultiStatus("<d:response><d:href>/x/</d:href></d:response>"));

        using (var ctx = db.NewContext())
            await NewCalDav(ctx, handler).SyncTaskAsync(
                new TaskItem { Id = 1, Title = "T", DueDate = DateTime.Today }, null);

        using var assert = db.NewContext();
        Assert.NotNull(await new CalDavConnectionRepository(assert).GetAsync());
    }

    [Fact]
    public async Task PersistentUnauthorized_DropsConnection()
    {
        using var db = new TestDatabase();
        await SeedCalDavAsync(db);
        var handler = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized));

        using (var ctx = db.NewContext())
            await NewCalDav(ctx, handler).SyncTaskAsync(
                new TaskItem { Id = 1, Title = "T", DueDate = DateTime.Today }, null);

        using var assert = db.NewContext();
        Assert.Null(await new CalDavConnectionRepository(assert).GetAsync());
    }

    // ── Hilfen ───────────────────────────────────────────────────────────────

    private static CalDavCalendarService NewCalDav(AppDbContext ctx, HttpMessageHandler? handler = null) =>
        new(handler is null ? new HttpClient() : new HttpClient(handler), new CalDavConnectionRepository(ctx),
            new TaskRepository(ctx), NullLogger<CalDavCalendarService>.Instance);

    private static async Task SeedCalDavAsync(TestDatabase db)
    {
        using var ctx = db.NewContext();
        await new CalDavConnectionRepository(ctx).SaveAsync(new CalDavConnection
        {
            ServerUrl = Server,
            Username = "max",
            AppPassword = "pw",
            CalendarUrl = CalendarUrl,
            CalendarName = "Persönlich",
            ConnectedAtUtc = DateTime.UtcNow
        });
    }

    private static async Task<int> SeedTaskAsync(TestDatabase db, string? eventId, DateTime? due = null)
    {
        using var ctx = db.NewContext();
        var task = new TaskItem { Title = "Zahnarzt", CalendarEventId = eventId, DueDate = due, CreatedAt = DateTime.Now };
        ctx.Tasks.Add(task);
        await ctx.SaveChangesAsync();
        return task.Id;
    }

    private static async Task<string?> EventIdAsync(TestDatabase db)
    {
        using var ctx = db.NewContext();
        return (await ctx.Tasks.SingleAsync()).CalendarEventId;
    }

    private static HttpResponseMessage MultiStatus(string responses) => new(HttpStatusCode.MultiStatus)
    {
        Content = new StringContent($"""
            <?xml version="1.0"?>
            <d:multistatus xmlns:d="DAV:" xmlns:c="urn:ietf:params:xml:ns:caldav">{responses}</d:multistatus>
            """, Encoding.UTF8, "application/xml")
    };

    private sealed class FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var response = respond(request);
            response.RequestMessage ??= request;
            return Task.FromResult(response);
        }
    }
}
