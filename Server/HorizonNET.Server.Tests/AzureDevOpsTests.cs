using System.Net;
using System.Text;
using System.Text.Json;
using HorizonNET.Api.Services;
using HorizonNET.Data.Repositories;
using HorizonNET.Domain.Entities;
using HorizonNET.Shared.Transfer.Enums;
using Microsoft.EntityFrameworkCore;

namespace HorizonNET.Server.Tests;

// Azure-DevOps-Import gegen eine nachgebaute REST-API (FakeHandler) und die echte
// In-Memory-SQLite. Geprüft wird, was am Ende als Task in der Datenbank steht.
public class AzureDevOpsTests
{
    private const string Org = "https://dev.azure.com/contoso";
    private const string Pat = "geheimer-pat-123";

    // ── Verbindung ───────────────────────────────────────────────────────────

    [Fact]
    public async Task ConnectionRepository_StoresPatEncrypted()
    {
        using var db = new TestDatabase();
        using (var act = db.NewContext())
            await new AzureDevOpsConnectionRepository(act).SaveAsync(new AzureDevOpsConnection
            {
                OrganizationUrl = Org, PersonalAccessToken = Pat, ConnectedAtUtc = DateTime.UtcNow
            });

        using (var raw = db.NewContext())
        {
            var conn = raw.Database.GetDbConnection();
            await conn.OpenAsync();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT PersonalAccessToken FROM AzureDevOpsConnections";
            var stored = (string?)await cmd.ExecuteScalarAsync();
            Assert.NotNull(stored);
            Assert.DoesNotContain("geheimer", stored);
        }

        using var assert = db.NewContext();
        Assert.Equal(Pat, (await new AzureDevOpsConnectionRepository(assert).GetAsync())!.PersonalAccessToken);
    }

    [Theory]
    [InlineData("https://dev.azure.com/contoso", "https://dev.azure.com/contoso")]
    [InlineData("dev.azure.com/contoso/", "https://dev.azure.com/contoso")]
    [InlineData("https://dev.azure.com/contoso/Projekt/_workitems/edit/5", "https://dev.azure.com/contoso")]
    [InlineData("https://contoso.visualstudio.com/Projekt", "https://contoso.visualstudio.com")]
    [InlineData("https://tfs.firma.de/tfs/DefaultCollection/Projekt", "https://tfs.firma.de/tfs/DefaultCollection")]
    [InlineData("http://dev.azure.com/contoso", null)]
    [InlineData("https://dev.azure.com/", null)]
    [InlineData("", null)]
    public void NormalizeOrganizationUrl(string input, string? expected) =>
        Assert.Equal(expected, AzureDevOpsService.NormalizeOrganizationUrl(input));

    [Fact]
    public async Task Connect_InvalidPat_ThrowsReadableError_AndSavesNothing()
    {
        using var db = new TestDatabase();
        var handler = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized));

        using (var act = db.NewContext())
        {
            var ex = await Assert.ThrowsAsync<AzureDevOpsException>(
                () => NewService(act, handler).ConnectAsync(Org, "falsch"));
            Assert.Contains("Token", ex.Message);
        }

        using var assert = db.NewContext();
        Assert.Null(await new AzureDevOpsConnectionRepository(assert).GetAsync());
    }

    // Ungültiger PAT kann auch als 203 mit HTML-Anmeldeseite kommen.
    [Fact]
    public async Task Connect_HtmlSignInPage_IsTreatedAsInvalidPat()
    {
        using var db = new TestDatabase();
        var handler = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.NonAuthoritativeInformation)
        {
            Content = new StringContent("<html>Sign in</html>", Encoding.UTF8, "text/html")
        });

        using var act = db.NewContext();
        await Assert.ThrowsAsync<AzureDevOpsException>(() => NewService(act, handler).ConnectAsync(Org, "falsch"));
    }

    [Fact]
    public async Task Connect_SendsBasicAuth_AndStoresDisplayName()
    {
        using var db = new TestDatabase();
        string? auth = null;
        var handler = new FakeHandler(req =>
        {
            auth = req.Headers.Authorization?.ToString();
            return Json(new { authenticatedUser = new { providerDisplayName = "Max Muster" } });
        });

        using (var act = db.NewContext())
            await NewService(act, handler).ConnectAsync(Org + "/", Pat);

        Assert.Equal("Basic " + Convert.ToBase64String(Encoding.ASCII.GetBytes(":" + Pat)), auth);
        using var assert = db.NewContext();
        var conn = await new AzureDevOpsConnectionRepository(assert).GetAsync();
        Assert.Equal(Org, conn!.OrganizationUrl);
        Assert.Equal("Max Muster", conn.DisplayName);
    }

    // ── Work Items ───────────────────────────────────────────────────────────

    [Fact]
    public async Task GetMyWorkItems_QueriesLinkedProject_AndMarksImported()
    {
        using var db = new TestDatabase();
        var projectId = await SeedAsync(db, alreadyImportedWorkItemId: 2);
        string? wiql = null;
        string? wiqlUrl = null;
        var handler = new FakeHandler(req =>
        {
            if (req.RequestUri!.AbsolutePath.EndsWith("/_apis/wit/wiql"))
            {
                wiqlUrl = req.RequestUri.AbsoluteUri;
                wiql = req.Content!.ReadAsStringAsync().Result;
                return Json(new { workItems = new[] { new { id = 2 }, new { id = 1 } } });
            }
            return WorkItemsResponse();
        });

        using var act = db.NewContext();
        var items = await NewService(act, handler).GetMyWorkItemsAsync(projectId, includeClosed: false);

        Assert.StartsWith($"{Org}/Mein%20Projekt/_apis/wit/wiql", wiqlUrl);
        Assert.Contains("@Me", wiql);
        Assert.Contains("NOT IN", wiql);
        Assert.Equal([2, 1], items.Select(i => i.Id)); // Reihenfolge der Abfrage
        Assert.True(items.Single(i => i.Id == 2).AlreadyImported);
        Assert.False(items.Single(i => i.Id == 1).AlreadyImported);
        Assert.Equal($"{Org}/Mein%20Projekt/_workitems/edit/1", items.Single(i => i.Id == 1).Url);
    }

    // Mit "Abgeschlossene" darf kein offenes Item herausfallen, auch wenn die
    // abgeschlossenen allein das $top-Limit füllen.
    [Fact]
    public async Task GetMyWorkItems_IncludeClosed_KeepsOpenItemsFirst()
    {
        using var db = new TestDatabase();
        var projectId = await SeedAsync(db);
        var queries = new List<string>();
        var handler = new FakeHandler(req =>
        {
            if (req.RequestUri!.AbsolutePath.EndsWith("/_apis/wit/wiql"))
            {
                var q = req.Content!.ReadAsStringAsync().Result;
                queries.Add(q);
                return q.Contains("NOT IN")
                    ? Json(new { workItems = new[] { new { id = 1 } } })
                    : Json(new { workItems = new[] { new { id = 2 }, new { id = 1 } } });
            }
            return WorkItemsResponse();
        });

        using var act = db.NewContext();
        var items = await NewService(act, handler).GetMyWorkItemsAsync(projectId, includeClosed: true);

        Assert.Equal(2, queries.Count);
        Assert.Equal([1, 2], items.Select(i => i.Id)); // offen zuerst, keine Dubletten
    }

    [Fact]
    public async Task GetMyWorkItems_UnlinkedProject_Throws()
    {
        using var db = new TestDatabase();
        var projectId = await SeedAsync(db, linked: false);

        using var act = db.NewContext();
        await Assert.ThrowsAsync<AzureDevOpsException>(
            () => NewService(act, new FakeHandler(_ => WorkItemsResponse())).GetMyWorkItemsAsync(projectId, false));
    }

    // ── Import ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task Import_CreatesTasksWithLink_SkipsImportedAndForeignProjects()
    {
        using var db = new TestDatabase();
        var projectId = await SeedAsync(db, alreadyImportedWorkItemId: 2);

        using (var act = db.NewContext())
        {
            var (created, skipped) = await NewService(act, new FakeHandler(_ => WorkItemsResponse()))
                .ImportAsync(projectId, [1, 2, 3]);
            Assert.Single(created);
            Assert.Equal(2, skipped); // 2 = schon importiert, 3 = anderes AzDO-Projekt
        }

        using var assert = db.NewContext();
        var task = await assert.Tasks.SingleAsync(t => t.AzureDevOpsWorkItemId == 1);
        Assert.Equal("#1 Login reparieren", task.Title);
        Assert.Equal($"{Org}/Mein%20Projekt/_workitems/edit/1", task.Link);
        Assert.Equal("Zeile eins\nZeile & zwei", task.Description);
        Assert.Equal(Priority.High, task.Priority);
        Assert.Equal(projectId, task.ProjectId);
        Assert.Equal(WorkStatus.Planned, task.Status);
    }

    // Mehr als 200 IDs (offen + abgeschlossen): Die workitems-API nimmt höchstens 200 je
    // Aufruf, also muss in mehreren Blöcken gelesen werden – ohne dass etwas fehlt.
    [Fact]
    public async Task GetMyWorkItems_MoreThan200_ReadsInChunks()
    {
        using var db = new TestDatabase();
        var projectId = await SeedAsync(db);
        var chunkSizes = new List<int>();
        var handler = new FakeHandler(req =>
        {
            var path = req.RequestUri!.AbsolutePath;
            if (path.EndsWith("/_apis/wit/wiql"))
            {
                var closed = !req.Content!.ReadAsStringAsync().Result.Contains("NOT IN");
                var range = closed ? Enumerable.Range(201, 150) : Enumerable.Range(1, 200);
                return Json(new { workItems = range.Select(id => new { id }).ToArray() });
            }
            if (path.EndsWith("/_apis/wit/workitems"))
            {
                var ids = System.Web.HttpUtility.ParseQueryString(req.RequestUri.Query)["ids"]!
                    .Split(',').Select(int.Parse).ToList();
                chunkSizes.Add(ids.Count);
                return Json(new
                {
                    value = ids.Select(id => new
                    {
                        id,
                        fields = new Dictionary<string, object>
                        {
                            ["System.Title"] = $"Item {id}", ["System.State"] = id > 200 ? "Closed" : "Active",
                            ["System.WorkItemType"] = "Task", ["System.TeamProject"] = "Mein Projekt"
                        }
                    }).ToArray()
                });
            }
            return Json(new { value = Array.Empty<object>() });
        });

        using var act = db.NewContext();
        var items = await NewService(act, handler).GetMyWorkItemsAsync(projectId, includeClosed: true);

        Assert.Equal(350, items.Count);
        Assert.Equal([200, 150], chunkSizes);
        Assert.Equal(Enumerable.Range(1, 350), items.Select(i => i.Id));
    }

    // Ein importierter Task im Papierkorb zählt weiter als "bereits importiert": Ein
    // erneuter Import legte sonst eine Dublette neben den wiederherstellbaren Task.
    [Fact]
    public async Task Import_TrashedTaskCountsAsImported()
    {
        using var db = new TestDatabase();
        var projectId = await SeedAsync(db);
        using (var seed = db.NewContext())
        {
            seed.Tasks.Add(new TaskItem
            {
                Title = "gelöscht", ProjectId = projectId, AzureDevOpsWorkItemId = 1,
                AzureDevOpsOrganizationUrl = Org, DeletedAt = DateTime.Now
            });
            await seed.SaveChangesAsync();
        }

        using (var act = db.NewContext())
        {
            var (created, skipped) = await NewService(act, new FakeHandler(_ => WorkItemsResponse()))
                .ImportAsync(projectId, [1]);
            Assert.Empty(created);
            Assert.Equal(1, skipped);
        }

        using var assert = db.NewContext();
        Assert.Equal(1, await assert.Tasks.IgnoreQueryFilters().CountAsync(t => t.AzureDevOpsWorkItemId == 1));
    }

    // Work-Item-IDs sind nur je Organisation eindeutig.
    [Fact]
    public async Task ImportedIds_AreScopedToOrganization()
    {
        using var db = new TestDatabase();
        var projectId = await SeedAsync(db);
        using (var seed = db.NewContext())
        {
            seed.Tasks.Add(new TaskItem
            {
                Title = "andere Org", ProjectId = projectId, AzureDevOpsWorkItemId = 1,
                AzureDevOpsOrganizationUrl = "https://dev.azure.com/andere-org"
            });
            await seed.SaveChangesAsync();
        }

        using (var act = db.NewContext())
        {
            var (created, _) = await NewService(act, new FakeHandler(_ => WorkItemsResponse()))
                .ImportAsync(projectId, [1]);
            Assert.Equal(Org, Assert.Single(created).AzureDevOpsOrganizationUrl);
        }
    }

    // Zwei parallele Imports desselben Items: Der eindeutige Index lässt nur einen durch,
    // der andere legt gar nichts an (Transaktion) und meldet sich verständlich.
    [Fact]
    public async Task Import_ConcurrentDuplicate_IsRejectedByUniqueIndex()
    {
        using var db = new TestDatabase();
        var projectId = await SeedAsync(db);

        using (var first = db.NewContext())
            Assert.True(await new TaskRepository(first).CreateImportedAsync(
                [new TaskItem { Title = "#1", ProjectId = projectId, AzureDevOpsWorkItemId = 1, AzureDevOpsOrganizationUrl = Org }]));

        using (var second = db.NewContext())
            Assert.False(await new TaskRepository(second).CreateImportedAsync(
            [
                new TaskItem { Title = "#5", ProjectId = projectId, AzureDevOpsWorkItemId = 5, AzureDevOpsOrganizationUrl = Org },
                new TaskItem { Title = "#1", ProjectId = projectId, AzureDevOpsWorkItemId = 1, AzureDevOpsOrganizationUrl = Org }
            ]));

        using var assert = db.NewContext();
        Assert.Equal(["#1"], await assert.Tasks.Select(t => t.Title).ToListAsync());
    }

    // Bugs tragen ihren Text je nach Prozessvorlage in den Reproschritten.
    [Fact]
    public async Task Import_Bug_FallsBackToReproSteps()
    {
        using var db = new TestDatabase();
        var projectId = await SeedAsync(db);
        var handler = new FakeHandler(_ => Json(new
        {
            value = new[]
            {
                new { id = 9, fields = new Dictionary<string, object>
                {
                    ["System.Title"] = "Absturz", ["System.State"] = "New", ["System.WorkItemType"] = "Bug",
                    ["System.TeamProject"] = "Mein Projekt",
                    ["Microsoft.VSTS.TCM.ReproSteps"] = "<div>Schritt 1</div>"
                } }
            }
        }));

        using var act = db.NewContext();
        var (created, _) = await NewService(act, handler).ImportAsync(projectId, [9]);
        Assert.Equal("Schritt 1", Assert.Single(created).Description);
    }

    [Fact]
    public async Task UnexpectedJson_IsReadableError()
    {
        using var db = new TestDatabase();
        var projectId = await SeedAsync(db);
        var handler = new FakeHandler(_ => Json(new { etwas = "anderes" }));

        using var act = db.NewContext();
        var ex = await Assert.ThrowsAsync<AzureDevOpsException>(
            () => NewService(act, handler).GetMyWorkItemsAsync(projectId, false));
        Assert.Contains("unerwartet", ex.Message);
    }

    [Fact]
    public async Task GetProjects_FollowsContinuationToken()
    {
        using var db = new TestDatabase();
        await SeedAsync(db);
        var handler = new FakeHandler(req =>
        {
            var second = req.RequestUri!.Query.Contains("continuationToken=weiter");
            var response = Json(new { value = new[] { new { name = second ? "Beta" : "Alpha" } } });
            if (!second) response.Headers.Add("x-ms-continuationtoken", "weiter");
            return response;
        });

        using var act = db.NewContext();
        Assert.Equal(["Alpha", "Beta"], await NewService(act, handler).GetProjectsAsync());
    }

    // Die abgeschlossenen Zustände kommen aus den Kategorien der Work-Item-Typen – so
    // stimmen sie auch für eigene Prozessvorlagen.
    [Fact]
    public void ParseClosedStates_UsesStateCategories()
    {
        using var doc = JsonDocument.Parse("""
            { "value": [
              { "name": "Task", "states": [
                { "name": "Neu", "category": "Proposed" },
                { "name": "In Arbeit", "category": "InProgress" },
                { "name": "Erledigt", "category": "Completed" } ] },
              { "name": "Bug", "states": [
                { "name": "Behoben", "category": "Resolved" },
                { "name": "Abgelehnt", "category": "Removed" } ] }
            ] }
            """);

        Assert.Equal(["Abgelehnt", "Behoben", "Erledigt"], AzureDevOpsService.ParseClosedStates(doc.RootElement));
    }

    [Theory]
    [InlineData(1, Priority.High)]
    [InlineData(2, Priority.Medium)]
    [InlineData(3, Priority.Low)]
    [InlineData(4, Priority.Low)]
    [InlineData(null, Priority.Medium)]
    public void MapPriority(int? azdo, Priority expected) =>
        Assert.Equal(expected, AzureDevOpsService.MapPriority(azdo));

    [Fact]
    public void HtmlToText_StripsTagsAndDecodes() =>
        Assert.Equal("Titel\n\nPunkt 1\nPunkt 2 <ok>",
            AzureDevOpsService.HtmlToText("<h1>Titel</h1><br><br><br><ul><li>Punkt 1</li><li>Punkt&nbsp;2 &lt;ok&gt;</li></ul>"));

    // ── Hilfen ───────────────────────────────────────────────────────────────

    private static AzureDevOpsService NewService(HorizonNET.Data.AppDbContext ctx, FakeHandler handler) =>
        new(new HttpClient(handler),
            new AzureDevOpsConnectionRepository(ctx),
            new TaskRepository(ctx),
            new ProjectRepository(ctx));

    private static async Task<int> SeedAsync(TestDatabase db, int? alreadyImportedWorkItemId = null, bool linked = true)
    {
        using var ctx = db.NewContext();
        ctx.AzureDevOpsConnections.Add(new AzureDevOpsConnection
        {
            OrganizationUrl = Org, PersonalAccessToken = Pat, ConnectedAtUtc = DateTime.UtcNow
        });
        var project = new Project { Name = "Horizon", AzureDevOpsProject = linked ? "Mein Projekt" : null };
        ctx.Projects.Add(project);
        await ctx.SaveChangesAsync();

        if (alreadyImportedWorkItemId is int id)
        {
            ctx.Tasks.Add(new TaskItem { Title = "alt", ProjectId = project.Id, AzureDevOpsWorkItemId = id });
            await ctx.SaveChangesAsync();
        }
        return project.Id;
    }

    // Antwort des workitems-Endpunkts: nur die angefragten IDs, wie die echte API.
    private static HttpResponseMessage WorkItemsResponse(HttpRequestMessage? req = null) => Json(new
    {
        value = new object[]
        {
            new { id = 1, fields = new Dictionary<string, object>
            {
                ["System.Title"] = "Login reparieren", ["System.State"] = "Active",
                ["System.WorkItemType"] = "Bug", ["System.TeamProject"] = "Mein Projekt",
                ["Microsoft.VSTS.Common.Priority"] = 1,
                ["System.Description"] = "<div>Zeile eins</div><div>Zeile &amp; zwei</div>"
            } },
            new { id = 2, fields = new Dictionary<string, object>
            {
                ["System.Title"] = "Schon da", ["System.State"] = "New",
                ["System.WorkItemType"] = "Task", ["System.TeamProject"] = "Mein Projekt"
            } },
            new { id = 3, fields = new Dictionary<string, object>
            {
                ["System.Title"] = "Fremd", ["System.State"] = "New",
                ["System.WorkItemType"] = "Task", ["System.TeamProject"] = "Anderes Projekt"
            } }
        }
    });

    private static HttpResponseMessage Json(object body) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json")
    };

    private sealed class FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(respond(request));
    }
}
