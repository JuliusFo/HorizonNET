using HorizonNET.Api.Controllers;
using HorizonNET.Api.Services;
using HorizonNET.Data;
using HorizonNET.Data.Repositories;
using HorizonNET.Domain.Entities;
using HorizonNET.Shared.Transfer.DTOs;
using HorizonNET.Shared.Transfer.Enums;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;

namespace HorizonNET.Server.Tests;

// Der TasksController direkt instanziiert – kein TestServer, kein HTTP. Geprüft wird
// die Schicht, die es sonst nirgends gibt: was der Controller aus einer Anfrage macht,
// BEVOR er das Repository ruft. Genau dort saß der Fehler, den diese Datei festhält.
//
// Echte Repositories über die In-Memory-SQLite (siehe TestDatabase) statt Attrappen:
// Die Frage ist ja gerade, was am Ende in der Datenbank steht – eine Attrappe würde
// bloß bestätigen, dass der Controller irgendetwas aufruft.
public class TasksControllerTests
{
    // ── Teil-Updates dürfen nichts anfassen, wonach nicht gefragt wurde ──────────
    //
    // Der Timer-Knopf schickt nur "starte/stoppe die Uhr". Lief das über den Vollersatz
    // UpdateAsync, baute der Controller sich dafür ein TaskItem aus den Feldern zusammen,
    // die er kannte – Link und "Warten auf" waren nicht dabei und wurden mit null
    // überschrieben. Ein Klick auf ▶ löschte damit beides.
    //
    // Der Task steht bewusst auf "Pausiert" mit gefülltem "Warten auf": genau die Lage,
    // in der man den Timer drückt, weil man trotz Wartens schon mal anfängt.

    [Fact]
    public async Task StartTimer_KeepsLinkAndWaitingFor()
    {
        using var db = new TestDatabase();
        var id = await SeedTaskAsync(db);

        using (var act = db.NewContext())
            Assert.IsType<OkObjectResult>(await NewController(act).StartTimer(id));

        using var assert = db.NewContext();
        var task = (await assert.Tasks.FindAsync(id))!;
        Assert.Equal(WorkStatus.InProgress, task.Status);
        Assert.Equal("https://example.org/ticket/42", task.Link);
        Assert.Equal("Rückmeldung von Anna", task.WaitingFor);
    }

    [Fact]
    public async Task StopTimer_KeepsLinkAndWaitingFor()
    {
        using var db = new TestDatabase();
        var id = await SeedTaskAsync(db);

        // Erst starten, dann stoppen – der Stop-Pfad greift nur bei "In Arbeit".
        using (var act = db.NewContext())
            await NewController(act).StartTimer(id);

        using (var act = db.NewContext())
            Assert.IsType<OkObjectResult>(await NewController(act).StopTimer(id));

        using var assert = db.NewContext();
        var task = (await assert.Tasks.FindAsync(id))!;
        Assert.Equal(WorkStatus.Paused, task.Status);
        Assert.Equal("https://example.org/ticket/42", task.Link);
        Assert.Equal("Rückmeldung von Anna", task.WaitingFor);
    }

    // Die Antwort trägt die Felder ebenfalls – sonst zeigte der Client sie nach dem Klick
    // als leer an, obwohl in der Datenbank noch alles steht.
    [Fact]
    public async Task StartTimer_ResponseCarriesLinkAndWaitingFor()
    {
        using var db = new TestDatabase();
        var id = await SeedTaskAsync(db);

        using var act = db.NewContext();
        var result = Assert.IsType<OkObjectResult>(await NewController(act).StartTimer(id));
        var dto = Assert.IsType<TaskResponseDto>(result.Value);

        Assert.Equal("https://example.org/ticket/42", dto.Link);
        Assert.Equal("Rückmeldung von Anna", dto.WaitingFor);
        Assert.NotNull(dto.RunningSince); // Uhr läuft
    }

    [Fact]
    public async Task StartTimer_UnknownTask_ReturnsNotFound()
    {
        using var db = new TestDatabase();

        using var act = db.NewContext();
        Assert.IsType<NotFoundResult>(await NewController(act).StartTimer(999));
    }

    private static async Task<int> SeedTaskAsync(TestDatabase db)
    {
        using var ctx = db.NewContext();
        var task = new TaskItem
        {
            Title = "Task",
            Status = WorkStatus.Paused,
            Link = "https://example.org/ticket/42",
            WaitingFor = "Rückmeldung von Anna"
        };
        ctx.Tasks.Add(task);
        await ctx.SaveChangesAsync();
        return task.Id;
    }

    private static TasksController NewController(AppDbContext ctx) =>
        new(new TaskRepository(ctx), new TimeEntryRepository(ctx), NewGoogleService(ctx));

    // Der GoogleCalendarService ist eine konkrete Klasse und damit ein Pflichtparameter
    // des Controllers. Ohne gespeicherte Verbindung steigt jeder Aufruf sofort wieder aus
    // (GetCredentialAsync liefert null) – es geht also nie etwas ins Netz. Die Zugangsdaten
    // sind trotzdem nötig, weil der Konstruktor sonst wirft.
    private static GoogleCalendarService NewGoogleService(AppDbContext ctx)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Google:ClientId"] = "test-client-id",
                ["Google:ClientSecret"] = "test-client-secret"
            })
            .Build();

        return new GoogleCalendarService(
            config, new GoogleConnectionRepository(ctx), new TaskRepository(ctx), new AppSettingRepository(ctx));
    }
}
