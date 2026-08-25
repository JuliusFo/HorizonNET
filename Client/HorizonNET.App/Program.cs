using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Microsoft.Extensions.Localization;
using HorizonNET.App;
using HorizonNET.App.Services;
using Radzen;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

// Die API ist der Origin, der die App ausliefert (Same-Origin-Hosting). Per
// ApiSettings:BaseUrl in wwwroot/appsettings.json übersteuerbar – z. B. um den Client
// standalone (dotnet run im Client-Projekt) gegen eine anders laufende API zu fahren.
var apiBaseUrl = builder.Configuration["ApiSettings:BaseUrl"]
    ?? builder.HostEnvironment.BaseAddress;

// Toast-Benachrichtigungen (Netzwerkfehler-Feedback, Undo)
builder.Services.AddScoped<ToastService>();

// Zentrale Lösch-Bestätigungen
builder.Services.AddScoped<ConfirmService>();

// HttpClient für den ApiService mit der konfigurierten Server-URL registrieren.
// Kette: ApiErrorHandler (Fehler → Toast, 401 → Login-Maske) →
// ApiCredentialsHandler (Auth-Cookie mitschicken) → Browser-fetch.
// AuthState kommt als späte Func in den Handler, weil er selbst am HttpClient hängt.
builder.Services.AddScoped(sp =>
{
    var handler = new ApiErrorHandler(
        sp.GetRequiredService<ToastService>(),
        () => sp.GetRequiredService<AuthState>())
    {
        InnerHandler = new ApiCredentialsHandler { InnerHandler = new HttpClientHandler() }
    };
    return new HttpClient(handler) { BaseAddress = new Uri(apiBaseUrl) };
});

// ApiService für Dependency Injection registrieren
builder.Services.AddScoped<ApiService>();

// Angemeldet-Zustand (Cookie-Sitzung); das MainLayout zeigt ohne Sitzung die Login-Maske
builder.Services.AddScoped<AuthState>();

// Radzen-Komponenten (u. a. für den Kalender-Scheduler)
builder.Services.AddRadzenComponents();

// Gemeinsamer Projekt-State (geteilt zwischen Navigationsleiste und Seiten)
builder.Services.AddScoped<ProjectState>();

// Gemeinsamer Arbeitsbereich-State
builder.Services.AddScoped<WorkspaceState>();

// Laufender Timer der Zeiterfassung (geteilt zwischen Navigation und Seiten)
builder.Services.AddScoped<TimerState>();

// Lokale UI-Einstellungen (localStorage)
builder.Services.AddScoped<SettingsState>();

// Bildschirmsperre des Journals (PIN + Auto-Lock). Bewusst nur UI-Schutz.
builder.Services.AddScoped<JournalLockState>();

// UI-Sounds (Web Audio via JS-Interop)
builder.Services.AddScoped<SoundService>();

// Client-/API-Version (Anzeige + Versatz-Erkennung beim Start)
builder.Services.AddScoped<VersionState>();

// Lokalisierung registrieren
builder.Services.AddLocalization(options => options.ResourcesPath = "Resources");

await builder.Build().RunAsync();
