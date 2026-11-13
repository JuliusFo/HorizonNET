using System.Xml.Linq;
using HorizonNET.Api.Services.Calendar;
using HorizonNET.Data.Repositories;
using HorizonNET.Domain.Entities;
using HorizonNET.Shared.Transfer.DTOs;
using HorizonNET.Shared.Transfer.Enums;
using Microsoft.EntityFrameworkCore;

namespace HorizonNET.Server.Tests;

// Nextcloud-/CalDAV-Anbindung ohne Netz: iCal-Erzeugung, das Lesen der WebDAV-Antworten
// (Beispiele im Format von Nextcloud/sabre) und die Datenhaltung rund um den Anbieterwechsel.
public class CalDavTests
{
    private static readonly HashSet<string> NoOwnIds = [];

    // ── iCal schreiben ─────────────────────────────────────────────────────────

    private static TaskItem Task(Action<TaskItem>? setup = null)
    {
        var t = new TaskItem
        {
            Id = 7,
            Title = "Zahnarzt",
            Description = "Kontrolle",
            DueDate = new DateTime(2026, 11, 20),
            Status = WorkStatus.Planned,
            CreatedAt = new DateTime(2026, 11, 1, 8, 0, 0)
        };
        setup?.Invoke(t);
        return t;
    }

    private static string Ics(TaskItem task, int? reminder = null) =>
        CalDavCalendarService.BuildICalendar("uid-1", TaskEventData.From(task, reminder),
            new DateTime(2026, 11, 13, 12, 0, 0, DateTimeKind.Utc));

    [Fact]
    public void BuildICalendar_AllDay_UsesDateValuesWithExclusiveEnd()
    {
        var ics = Ics(Task());

        Assert.Contains("BEGIN:VEVENT", ics);
        Assert.Contains("UID:uid-1", ics);
        Assert.Contains("SUMMARY:Zahnarzt", ics);
        Assert.Contains("DTSTART;VALUE=DATE:20261120", ics);
        Assert.Contains("DTEND;VALUE=DATE:20261121", ics);
        Assert.Contains("TRANSP:OPAQUE", ics);
        Assert.DoesNotContain("BEGIN:VALARM", ics);
    }

    [Fact]
    public void BuildICalendar_WithTimes_WritesUtc()
    {
        var task = Task(t =>
        {
            t.StartTime = new DateTime(2026, 11, 20, 9, 30, 0);
            t.EndTime = new DateTime(2026, 11, 20, 10, 15, 0);
        });

        var ics = Ics(task);
        var start = new DateTime(2026, 11, 20, 9, 30, 0).ToUniversalTime();

        Assert.Contains($"DTSTART:{start:yyyyMMdd'T'HHmmss}Z", ics);
        Assert.DoesNotContain("VALUE=DATE", ics);
    }

    [Fact]
    public void BuildICalendar_Reminder_AddsAlarm()
    {
        var ics = Ics(Task(), reminder: 15);

        Assert.Contains("BEGIN:VALARM", ics);
        Assert.Contains("ACTION:DISPLAY", ics);
        Assert.Contains("TRIGGER:-PT15M", ics);
    }

    // Wie bei Google: Erledigt heißt Haken im Titel, "frei" und keine Erinnerung –
    // auch wenn eine Standard-Erinnerung eingestellt ist.
    [Fact]
    public void BuildICalendar_Completed_IsDefused()
    {
        var ics = Ics(Task(t => t.Status = WorkStatus.Done), reminder: 15);

        Assert.Contains("SUMMARY:✓ Zahnarzt", ics);
        Assert.Contains("TRANSP:TRANSPARENT", ics);
        Assert.DoesNotContain("BEGIN:VALARM", ics);
    }

    [Fact]
    public void NewUid_IsStablePerTask()
    {
        Assert.Equal(CalDavCalendarService.NewUid(Task()), CalDavCalendarService.NewUid(Task()));
        Assert.NotEqual(CalDavCalendarService.NewUid(Task()),
            CalDavCalendarService.NewUid(Task(t => t.CreatedAt = t.CreatedAt.AddSeconds(1))));
    }

    // ── Termine lesen ──────────────────────────────────────────────────────────

    private const string ReportResponse = """
        <?xml version="1.0"?>
        <d:multistatus xmlns:d="DAV:" xmlns:cal="urn:ietf:params:xml:ns:caldav">
          <d:response>
            <d:href>/remote.php/dav/calendars/max/personal/abc.ics</d:href>
            <d:propstat>
              <d:prop>
                <cal:calendar-data>BEGIN:VCALENDAR
        VERSION:2.0
        PRODID:-//Sabre//Sabre VObject 4.5.6//EN
        BEGIN:VEVENT
        UID:fremd-1
        DTSTART:20261120T080000Z
        DTEND:20261120T090000Z
        SUMMARY:Team-Meeting
        END:VEVENT
        END:VCALENDAR
        </cal:calendar-data>
              </d:prop>
              <d:status>HTTP/1.1 200 OK</d:status>
            </d:propstat>
          </d:response>
          <d:response>
            <d:href>/remote.php/dav/calendars/max/personal/urlaub.ics</d:href>
            <d:propstat>
              <d:prop>
                <cal:calendar-data>BEGIN:VCALENDAR
        VERSION:2.0
        BEGIN:VEVENT
        UID:fremd-2
        DTSTART;VALUE=DATE:20261123
        DTEND;VALUE=DATE:20261125
        SUMMARY:Urlaub
        END:VEVENT
        END:VCALENDAR
        </cal:calendar-data>
              </d:prop>
              <d:status>HTTP/1.1 200 OK</d:status>
            </d:propstat>
          </d:response>
          <d:response>
            <d:href>/remote.php/dav/calendars/max/personal/horizonnet-task-7-1.ics</d:href>
            <d:propstat>
              <d:prop>
                <cal:calendar-data>BEGIN:VCALENDAR
        VERSION:2.0
        BEGIN:VEVENT
        UID:horizonnet-task-7-1
        DTSTART;VALUE=DATE:20261120
        DTEND;VALUE=DATE:20261121
        SUMMARY:Eigener Task
        END:VEVENT
        END:VCALENDAR
        </cal:calendar-data>
              </d:prop>
              <d:status>HTTP/1.1 200 OK</d:status>
            </d:propstat>
          </d:response>
        </d:multistatus>
        """;

    [Fact]
    public void ParseEvents_ReadsTimedAndAllDayEvents()
    {
        var events = CalDavCalendarService.ParseEvents(ReportResponse, NoOwnIds);

        Assert.Equal(3, events.Count);

        var meeting = events.Single(e => e.Title == "Team-Meeting");
        Assert.False(meeting.AllDay);
        Assert.Equal(new DateTime(2026, 11, 20, 8, 0, 0, DateTimeKind.Utc).ToLocalTime(), meeting.Start);
        Assert.Equal(TimeSpan.FromHours(1), meeting.End - meeting.Start);

        var urlaub = events.Single(e => e.Title == "Urlaub");
        Assert.True(urlaub.AllDay);
        Assert.Equal(new DateTime(2026, 11, 23), urlaub.Start);
        Assert.Equal(new DateTime(2026, 11, 25), urlaub.End);
    }

    // Selbst gespiegelte Tasks dürfen nicht doppelt erscheinen (Task + sein Termin).
    [Fact]
    public void ParseEvents_HidesOwnEvents()
    {
        var events = CalDavCalendarService.ParseEvents(ReportResponse, new HashSet<string> { "horizonnet-task-7-1" });

        Assert.DoesNotContain(events, e => e.Title == "Eigener Task");
        Assert.Equal(2, events.Count);
    }

    // ── Discovery ──────────────────────────────────────────────────────────────

    private static readonly Uri Home = new("https://cloud.example.org/remote.php/dav/calendars/max/");

    private const string CalendarListResponse = """
        <?xml version="1.0"?>
        <d:multistatus xmlns:d="DAV:" xmlns:cal="urn:ietf:params:xml:ns:caldav" xmlns:cs="http://calendarserver.org/ns/">
          <d:response>
            <d:href>/remote.php/dav/calendars/max/</d:href>
            <d:propstat>
              <d:prop><d:resourcetype><d:collection/></d:resourcetype></d:prop>
              <d:status>HTTP/1.1 200 OK</d:status>
            </d:propstat>
          </d:response>
          <d:response>
            <d:href>/remote.php/dav/calendars/max/personal/</d:href>
            <d:propstat>
              <d:prop>
                <d:resourcetype><d:collection/><cal:calendar/></d:resourcetype>
                <d:displayname>Persönlich</d:displayname>
                <cal:supported-calendar-component-set><cal:comp name="VEVENT"/></cal:supported-calendar-component-set>
                <d:current-user-privilege-set><d:privilege><d:read/></d:privilege><d:privilege><d:write/></d:privilege></d:current-user-privilege-set>
              </d:prop>
              <d:status>HTTP/1.1 200 OK</d:status>
            </d:propstat>
          </d:response>
          <d:response>
            <d:href>/remote.php/dav/calendars/max/tasks/</d:href>
            <d:propstat>
              <d:prop>
                <d:resourcetype><d:collection/><cal:calendar/></d:resourcetype>
                <d:displayname>Aufgaben</d:displayname>
                <cal:supported-calendar-component-set><cal:comp name="VTODO"/></cal:supported-calendar-component-set>
              </d:prop>
              <d:status>HTTP/1.1 200 OK</d:status>
            </d:propstat>
          </d:response>
          <d:response>
            <d:href>/remote.php/dav/calendars/max/geteilt_shared_by_anna/</d:href>
            <d:propstat>
              <d:prop>
                <d:resourcetype><d:collection/><cal:calendar/></d:resourcetype>
                <d:displayname>Annas Kalender</d:displayname>
                <cal:supported-calendar-component-set><cal:comp name="VEVENT"/></cal:supported-calendar-component-set>
                <d:current-user-privilege-set><d:privilege><d:read/></d:privilege></d:current-user-privilege-set>
              </d:prop>
              <d:status>HTTP/1.1 200 OK</d:status>
            </d:propstat>
          </d:response>
          <d:response>
            <d:href>/remote.php/dav/calendars/max/inbox/</d:href>
            <d:propstat>
              <d:prop><d:resourcetype><d:collection/><cal:schedule-inbox/></d:resourcetype></d:prop>
              <d:status>HTTP/1.1 200 OK</d:status>
            </d:propstat>
          </d:response>
        </d:multistatus>
        """;

    // Nur der beschreibbare Terminkalender bleibt: keine Aufgabenliste, kein read-only
    // geteilter Kalender, keine Scheduling-Inbox.
    [Fact]
    public void ParseCalendars_KeepsOnlyWritableEventCalendars()
    {
        var calendars = CalDavCalendarService.ParseCalendars(XDocument.Parse(CalendarListResponse), Home);

        var cal = Assert.Single(calendars);
        Assert.Equal("Persönlich", cal.Name);
        Assert.Equal("https://cloud.example.org/remote.php/dav/calendars/max/personal/", cal.Url);
    }

    [Fact]
    public void FindHref_ResolvesRelativeToRequestUrl()
    {
        var xml = XDocument.Parse("""
            <d:multistatus xmlns:d="DAV:">
              <d:response><d:href>/remote.php/dav/</d:href><d:propstat><d:prop>
                <d:current-user-principal><d:href>/remote.php/dav/principals/users/max/</d:href></d:current-user-principal>
              </d:prop><d:status>HTTP/1.1 200 OK</d:status></d:propstat></d:response>
            </d:multistatus>
            """);

        var href = CalDavCalendarService.FindHref(xml, new Uri("https://cloud.example.org/remote.php/dav/"),
            XName.Get("current-user-principal", "DAV:"));

        Assert.Equal("https://cloud.example.org/remote.php/dav/principals/users/max/", href!.ToString());
    }

    [Theory]
    [InlineData("cloud.example.org", "https://cloud.example.org/")]
    [InlineData("https://cloud.example.org/nextcloud", "https://cloud.example.org/nextcloud/")]
    [InlineData("http://192.168.1.5:8080/", "http://192.168.1.5:8080/")]
    [InlineData("ftp://cloud.example.org", null)]
    [InlineData("  ", null)]
    public void NormalizeServerUrl(string input, string? expected) =>
        Assert.Equal(expected, CalDavCalendarService.NormalizeServerUrl(input)?.ToString());

    // ── Datenhaltung ───────────────────────────────────────────────────────────

    [Fact]
    public async Task CalDavConnection_StoresPasswordEncrypted()
    {
        using var db = new TestDatabase();

        using (var act = db.NewContext())
            await new CalDavConnectionRepository(act).SaveAsync(new CalDavConnection
            {
                ServerUrl = "https://cloud.example.org",
                Username = "max",
                AppPassword = "geheimes-app-passwort",
                CalendarUrl = "https://cloud.example.org/remote.php/dav/calendars/max/personal/",
                CalendarName = "Persönlich",
                ConnectedAtUtc = DateTime.UtcNow
            });

        using (var raw = db.NewContext())
        {
            var conn = raw.Database.GetDbConnection();
            await conn.OpenAsync();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT AppPassword FROM CalDavConnections LIMIT 1";
            var stored = (string?)await cmd.ExecuteScalarAsync();
            Assert.DoesNotContain("geheimes", stored);
        }

        using var assert = db.NewContext();
        Assert.Equal("geheimes-app-passwort", (await new CalDavConnectionRepository(assert).GetAsync())!.AppPassword);
    }

    // Beim Anbieterwechsel gelten die alten Event-IDs nicht mehr – auch nicht die von
    // Tasks im Papierkorb, die sonst beim Wiederherstellen den neuen Kalender ansprächen.
    [Fact]
    public async Task ClearAllCalendarEventIds_AlsoClearsTrashedTasks()
    {
        using var db = new TestDatabase();

        using (var seed = db.NewContext())
        {
            seed.Tasks.Add(new TaskItem { Title = "aktiv", CalendarEventId = "a" });
            seed.Tasks.Add(new TaskItem { Title = "gelöscht", CalendarEventId = "b", DeletedAt = DateTime.UtcNow });
            await seed.SaveChangesAsync();
        }

        using (var act = db.NewContext())
            await new TaskRepository(act).ClearAllCalendarEventIdsAsync();

        using var assert = db.NewContext();
        Assert.All(await assert.Tasks.IgnoreQueryFilters().ToListAsync(), t => Assert.Null(t.CalendarEventId));
    }

    [Fact]
    public async Task CalendarSync_PicksNextcloudWhenConnected()
    {
        using var db = new TestDatabase();
        using var ctx = db.NewContext();
        var calendar = TasksControllerTests.NewCalendarService(ctx);

        Assert.Null(await calendar.GetActiveProviderAsync());
        Assert.False((await calendar.GetStatusAsync()).Connected);

        await new CalDavConnectionRepository(ctx).SaveAsync(new CalDavConnection
        {
            ServerUrl = "https://cloud.example.org",
            Username = "max",
            AppPassword = "pw",
            CalendarUrl = "https://cloud.example.org/remote.php/dav/calendars/max/personal/",
            CalendarName = "Persönlich"
        });

        var status = await calendar.GetStatusAsync();
        Assert.Equal(CalendarProviders.Nextcloud, status.Provider);
        Assert.Equal("max", status.Account);
        Assert.Equal("Persönlich", status.CalendarName);
        // Ohne Google-Credentials in der Konfiguration bietet die UI Google nicht an.
        Assert.False(status.GoogleAvailable);

        await calendar.DisconnectAsync();
        Assert.Null(await calendar.GetActiveProviderAsync());
    }
}
