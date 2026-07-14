using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Microsoft.Extensions.Localization;
using HorizonNET.App;
using HorizonNET.App.Services;
using Radzen;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

// Basis-URL aus appsettings.json lesen
var apiBaseUrl = builder.Configuration["ApiSettings:BaseUrl"]
    ?? throw new InvalidOperationException("ApiSettings:BaseUrl ist nicht konfiguriert.");

// Toast-Benachrichtigungen (Netzwerkfehler-Feedback, Undo)
builder.Services.AddScoped<ToastService>();

// Zentrale Lösch-Bestätigungen
builder.Services.AddScoped<ConfirmService>();

// HttpClient für den ApiService mit der konfigurierten Server-URL registrieren.
// Ein DelegatingHandler meldet fehlgeschlagene Aufrufe zentral als Toast.
builder.Services.AddScoped(sp =>
{
    var handler = new ApiErrorHandler(sp.GetRequiredService<ToastService>())
    {
        InnerHandler = new HttpClientHandler()
    };
    return new HttpClient(handler) { BaseAddress = new Uri(apiBaseUrl) };
});

// ApiService für Dependency Injection registrieren
builder.Services.AddScoped<ApiService>();

// Radzen-Komponenten (u. a. für den Kalender-Scheduler)
builder.Services.AddRadzenComponents();

// Gemeinsamer Projekt-State (geteilt zwischen Navigationsleiste und Seiten)
builder.Services.AddScoped<ProjectState>();

// Gemeinsamer Arbeitsbereich-State
builder.Services.AddScoped<WorkspaceState>();

// Lokale UI-Einstellungen (localStorage)
builder.Services.AddScoped<SettingsState>();

// UI-Sounds (Web Audio via JS-Interop)
builder.Services.AddScoped<SoundService>();

// Lokalisierung registrieren
builder.Services.AddLocalization(options => options.ResourcesPath = "Resources");

await builder.Build().RunAsync();
