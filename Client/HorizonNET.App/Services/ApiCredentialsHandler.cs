using Microsoft.AspNetCore.Components.WebAssembly.Http;

namespace HorizonNET.App.Services;

// Schickt das HttpOnly-Auth-Cookie bei jedem API-Aufruf mit. Nötig, solange Client und
// API auf verschiedenen Origins laufen (lokal: zwei Ports) – fetch lässt Cookies bei
// Cross-Origin-Aufrufen sonst weg. Wird mit dem Same-Origin-Hosting beim Livegang
// überflüssig, schadet dann aber auch nicht.
public class ApiCredentialsHandler : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        request.SetBrowserRequestCredentials(BrowserRequestCredentials.Include);
        return base.SendAsync(request, cancellationToken);
    }
}
