using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Microsoft.Extensions.Localization;
using HorizonNET.App;
using HorizonNET.App.Services;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

// Basis-URL aus appsettings.json lesen
var apiBaseUrl = builder.Configuration["ApiSettings:BaseUrl"]
    ?? throw new InvalidOperationException("ApiSettings:BaseUrl ist nicht konfiguriert.");

// HttpClient für den ApiService mit der konfigurierten Server-URL registrieren
builder.Services.AddScoped(_ => new HttpClient { BaseAddress = new Uri(apiBaseUrl) });

// ApiService für Dependency Injection registrieren
builder.Services.AddScoped<ApiService>();

// Lokalisierung registrieren
builder.Services.AddLocalization(options => options.ResourcesPath = "Resources");

await builder.Build().RunAsync();
