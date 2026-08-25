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
//
// auth kommt als Func, nicht als Instanz: Der Handler wird beim Bau des HttpClient
// erzeugt, AuthState hängt aber (über den ApiService) selbst am HttpClient – die
// späte Auflösung durchbricht diesen Kreis.
public class ApiErrorHandler(ToastService toast, Func<AuthState> auth) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var isGet = request.Method == HttpMethod.Get;

        // Die Auth-Endpunkte werten ihre Statuscodes selbst aus (401 heißt dort
        // "falsches Passwort" bzw. "keine Sitzung", nicht "Fehler") – nichts anfassen.
        var isAuthCall = request.RequestUri?.AbsolutePath
            .StartsWith("/api/auth/", StringComparison.OrdinalIgnoreCase) == true;

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

        if (isAuthCall || response.IsSuccessStatusCode)
            return response;

        // Sitzung abgelaufen (oder nie angemeldet): Login-Maske statt Fehlertoast.
        // Bei parallel laufenden Aufrufen meldet nur der erste die abgelaufene Sitzung.
        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            var authState = auth();
            if (authState.IsLoggedIn)
                toast.ShowError("Sitzung abgelaufen – bitte neu anmelden.");
            authState.NotifySessionExpired();

            if (!isGet)
                return response;

            response.Dispose();
            return EmptyJson(request);
        }

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
