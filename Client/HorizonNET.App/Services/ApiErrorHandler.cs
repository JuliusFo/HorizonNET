using System.Net;
using System.Text;

namespace HorizonNET.App.Services;

// Fängt fehlgeschlagene HTTP-Aufrufe zentral ab und meldet sie als Toast.
// Dadurch werden bisher stille Fehler sichtbar (viele Api.*Async liefern bei
// Misserfolg nur null/false).
//
// Zusätzlich wird verhindert, dass ein fehlgeschlagener Aufruf die Blazor-
// Fehlerseite auslöst:
//  • GET: bei Fehler wird eine leere JSON-Antwort (200 "null") zurückgegeben,
//    damit GetFromJsonAsync sauber null liefert statt zu werfen.
//  • POST/PUT/DELETE: bei Verbindungsabbruch wird 503 zurückgegeben, damit die
//    ApiService-Methoden regulär null/false liefern.
public class ApiErrorHandler(ToastService toast) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var isGet = request.Method == HttpMethod.Get;

        HttpResponseMessage response;
        try
        {
            response = await base.SendAsync(request, cancellationToken);
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            // Server nicht erreichbar.
            toast.ShowError("Keine Verbindung zum Server.");
            return isGet
                ? EmptyJson(request)
                : new HttpResponseMessage(HttpStatusCode.ServiceUnavailable) { RequestMessage = request };
        }

        if (response.IsSuccessStatusCode)
            return response;

        // Server erreichbar, aber Fehlerstatus.
        if (isGet)
        {
            // 404 = „nicht gefunden": wird von der Seite lokal behandelt, kein Toast.
            if (response.StatusCode != HttpStatusCode.NotFound)
                toast.ShowError("Daten konnten nicht geladen werden.");
            response.Dispose();
            return EmptyJson(request);
        }

        toast.ShowError("Änderung konnte nicht gespeichert werden.");
        return response;
    }

    // Synthetische 200-Antwort mit JSON-null, damit GetFromJsonAsync null liefert.
    private static HttpResponseMessage EmptyJson(HttpRequestMessage request) =>
        new(HttpStatusCode.OK)
        {
            RequestMessage = request,
            Content        = new StringContent("null", Encoding.UTF8, "application/json")
        };
}
