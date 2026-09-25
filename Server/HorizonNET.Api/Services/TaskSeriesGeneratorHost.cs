namespace HorizonNET.Api.Services;

// Lässt den Serien-Generator beim App-Start und danach täglich kurz nach Mitternacht
// laufen. Serverseitig statt "beim Kalender-Laden", weil die App als Dienst läuft und
// Heute-Seite wie Google-Sync die Termine auch dann brauchen, wenn niemand den Kalender
// öffnet. Ein Lauf ist idempotent (siehe MaterializeAsync) – doppelt laufen schadet nicht.
public class TaskSeriesGeneratorHost(
    IServiceScopeFactory scopes,
    ILogger<TaskSeriesGeneratorHost> logger) : BackgroundService
{
    // Kurz nach Tagesbeginn, damit der neue Tag am Horizont sicher schon "heute" ist.
    private static readonly TimeSpan DailyAt = new(0, 5, 0);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Beim Start einmal – nach kurzer Pause, damit Migration und Seed sicher durch sind.
        await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
        await RunSafelyAsync(stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(UntilNextRun(DateTime.Now), stoppingToken);
            await RunSafelyAsync(stoppingToken);
        }
    }

    private static TimeSpan UntilNextRun(DateTime now)
    {
        var next = now.Date.Add(DailyAt);
        if (next <= now) next = next.AddDays(1);
        return next - now;
    }

    private async Task RunSafelyAsync(CancellationToken ct)
    {
        try
        {
            // Der Generator ist scoped (DbContext, Google) – pro Lauf ein eigener Scope.
            using var scope = scopes.CreateScope();
            await scope.ServiceProvider.GetRequiredService<TaskSeriesGenerator>().RunAsync(ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Herunterfahren – kein Fehler.
        }
        catch (Exception ex)
        {
            // Ein Fehler darf den Dienst nicht beenden; morgen ist ein neuer Lauf.
            logger.LogError(ex, "Terminserien-Generator fehlgeschlagen.");
        }
    }
}
