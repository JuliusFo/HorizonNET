using System.Reflection;
using HorizonNET.Api.Services;
using Microsoft.AspNetCore.DataProtection;
using HorizonNET.Data;
using HorizonNET.Data.Repositories;
using HorizonNET.Domain.Interfaces;
using System.Threading.RateLimiting;
using HorizonNET.Shared.Transfer.DTOs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

// Betrieb als Windows-Dienst auf dem vServer: meldet Start/Stopp an den Dienstmanager
// und setzt das Arbeitsverzeichnis auf den App-Ordner (Dienste starten sonst in
// System32 – der relative DB-Pfad ginge daneben). Außerhalb eines Dienstes ein No-op,
// lokales dotnet run bleibt unverändert.
builder.Host.UseWindowsService();

// Lokale Secrets (Google-Credentials etc.) – Datei ist per .gitignore ausgeschlossen.
// Bewusst NUR in der Entwicklung: In Produktion kommen Secrets als Umgebungsvariablen
// des Dienstes (Google__ClientId, Google__ClientSecret, Auth__…; "__" ersetzt den
// Doppelpunkt). Und da dieses AddJsonFile NACH dem eingebauten Env-Var-Provider hängt,
// würde eine liegengebliebene Datei auf dem Server sonst still die Env-Vars übersteuern.
if (builder.Environment.IsDevelopment())
    builder.Configuration.AddJsonFile("appsettings.Secrets.json", optional: true, reloadOnChange: true);

// Controller und Validierung
builder.Services.AddControllers();

// Eingebaute .NET 10 OpenAPI-Unterstützung
builder.Services.AddOpenApi();

// Schlüsselmaterial für verschlüsselte Spalten (Phase 12a, siehe EncryptedConverter).
// Der Ring liegt bewusst AUSSERHALB des Repos – Standard ist %LOCALAPPDATA%\HorizonNET\keys,
// überschreibbar per DataProtection:KeyRingPath.
//
// ⚠ Dieser Ordner gehört ins Backup. Ohne ihn sind alle verschlüsselten Werte verloren,
// und es gibt keinen Wiederherstellungsweg (siehe docs/konzept-journal.md).
var keyRingPath = builder.Configuration["DataProtection:KeyRingPath"]
    ?? Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "HorizonNET", "keys");

var dataProtection = builder.Services.AddDataProtection()
    // Fester Name: Er geht in die Schlüsselableitung ein. Ändert er sich, lässt sich
    // Bestehendes nicht mehr entschlüsseln.
    .SetApplicationName("HorizonNET")
    .PersistKeysToFileSystem(new DirectoryInfo(keyRingPath));

// Zusätzlich ans Windows-Benutzerkonto binden: Der Schlüsselring allein nützt dann
// nichts, wenn jemand nur die Dateien kopiert. Bei einem späteren Umzug auf Linux
// (Container) muss das durch ein Zertifikat ersetzt werden.
if (OperatingSystem.IsWindows())
    dataProtection.ProtectKeysWithDpapi();

// EF Core mit SQLite – Datenbankpfad aus appsettings.json
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("DefaultConnection")));

// Repository-Pattern per Dependency Injection registrieren
builder.Services.AddScoped<IWorkspaceRepository, WorkspaceRepository>();
builder.Services.AddScoped<IProjectRepository, ProjectRepository>();
builder.Services.AddScoped<ITaskRepository, TaskRepository>();
builder.Services.AddScoped<IGoogleConnectionRepository, GoogleConnectionRepository>();
builder.Services.AddScoped<IAppSettingRepository, AppSettingRepository>();
builder.Services.AddScoped<INoteRepository, NoteRepository>();
builder.Services.AddScoped<INoteFolderRepository, NoteFolderRepository>();
builder.Services.AddScoped<IDailyTaskRepository, DailyTaskRepository>();
builder.Services.AddScoped<ITaskTemplateRepository, TaskTemplateRepository>();
builder.Services.AddScoped<ITimeEntryRepository, TimeEntryRepository>();
builder.Services.AddScoped<IExerciseRepository, ExerciseRepository>();
builder.Services.AddScoped<IExerciseSetRepository, ExerciseSetRepository>();
builder.Services.AddScoped<IBodyWeightRepository, BodyWeightRepository>();
builder.Services.AddScoped<IJournalRepository, JournalRepository>();
builder.Services.AddScoped<IJournalTemplateRepository, JournalTemplateRepository>();

// Google-Kalender-Anbindung (OAuth + späterer Calendar-Zugriff)
builder.Services.AddScoped<GoogleCalendarService>();

// Authentifizierung: ein lokales Benutzerkonto (ASP.NET Core Identity) mit Cookie-Sitzung.
// Das volle AddIdentity statt AddIdentityCore, weil es Cookie-Schemata und
// Security-Stamp-Validierung komplett verdrahtet; Rollen bleiben schlicht ungenutzt.
builder.Services.AddIdentity<IdentityUser, IdentityRole>()
    .AddEntityFrameworkStores<AppDbContext>();

// Bremse für den Login: Er ist der einzige anonym erreichbare Endpunkt mit
// Passwort-Prüfung und damit das natürliche Ziel für Durchprobieren. 5 Versuche pro
// Minute und Absender-IP – Identitys Konto-Lockout (nach 5 Fehlversuchen) bleibt als
// zweite Schicht dahinter. Dank ForwardedHeaders ist die IP hinter dem Tunnel die des
// echten Besuchers, nicht die von cloudflared.
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("login", context =>
        RateLimitPartition.GetFixedWindowLimiter(
            context.Connection.RemoteIpAddress?.ToString() ?? "unbekannt",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 5,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0
            }));
});

// Fallback-Policy: Jeder Endpunkt OHNE eigene Auth-Angabe verlangt einen angemeldeten
// Benutzer. So ist "geschützt" der Standard und Ausnahmen ([AllowAnonymous]) sind
// explizit – ein vergessenes [Authorize] kann kein Loch mehr reißen.
builder.Services.AddAuthorization(options =>
    options.FallbackPolicy = new AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .Build());

builder.Services.ConfigureApplicationCookie(options =>
{
    options.Cookie.Name = "HorizonNET.Auth";
    // HttpOnly ist Standard (JS kommt nicht an den Cookie); Secure erzwingen, weil die
    // API ohnehin nur über HTTPS läuft – lokal wie hinter dem Tunnel.
    options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
    options.ExpireTimeSpan = TimeSpan.FromDays(14);
    options.SlidingExpiration = true;

    // Identity ist auf Websites mit Login-SEITE ausgelegt und würde per 302 dorthin
    // umleiten. Für eine API sind Statuscodes richtig – der Blazor-Client reagiert
    // auf 401 selbst (Paket c).
    options.Events.OnRedirectToLogin = ctx =>
    {
        ctx.Response.StatusCode = StatusCodes.Status401Unauthorized;
        return Task.CompletedTask;
    };
    options.Events.OnRedirectToAccessDenied = ctx =>
    {
        ctx.Response.StatusCode = StatusCodes.Status403Forbidden;
        return Task.CompletedTask;
    };
});

var app = builder.Build();

// Hinter dem Cloudflare Tunnel erreicht jede Anfrage die API als "http von localhost" –
// erst die X-Forwarded-Header tragen, wie der Besucher wirklich kam (https, Domain).
// Ohne diese Middleware baut die API falsche absolute URLs (Google-Redirect-URI,
// OAuth-Rücksprung) und hielte die Verbindung für unverschlüsselt.
//
// Ganz vorn in der Pipeline, damit HTTPS-Redirect, HSTS und Cookies schon den korrigierten
// Request sehen. Vertraut wird per Voreinstellung nur Loopback-Absendern – genau dort
// läuft cloudflared; von fremden Adressen werden die Header ignoriert.
app.UseForwardedHeaders(new ForwardedHeadersOptions
{
    ForwardedHeaders = ForwardedHeaders.XForwardedFor
                     | ForwardedHeaders.XForwardedProto
                     | ForwardedHeaders.XForwardedHost
});

// Sicherheits-Header an jeder Antwort (auch statischen Dateien – deshalb VOR
// UseStaticFiles). Die CSP ist auf das zugeschnitten, was die App wirklich lädt:
//  • script-src ohne 'unsafe-inline': Seit die index.html ohne Inline-Importmap
//    auskommt (Klarnamen statt Platzhalter-Transformation), gibt es keine
//    Inline-Skripte mehr – eingeschleustes Inline-JS wird damit blockiert.
//  • data:/blob: bei img/media für Notiz-Thumbnails, Zeichnungen und Sounds.
//  • frame-ancestors 'none' (+ X-Frame-Options): niemand bettet die App in Iframes ein.
// Scalar (nur Dev) bringt eigene Inline-Ressourcen mit und ist ausgenommen.
app.Use(async (context, next) =>
{
    var headers = context.Response.Headers;
    headers["X-Content-Type-Options"] = "nosniff";
    headers["Referrer-Policy"] = "no-referrer";
    headers["X-Frame-Options"] = "DENY";

    if (!context.Request.Path.StartsWithSegments("/scalar")
        && !context.Request.Path.StartsWithSegments("/openapi"))
    {
        headers["Content-Security-Policy"] =
            "default-src 'self'; " +
            "script-src 'self' 'wasm-unsafe-eval'; " +
            "style-src 'self' 'unsafe-inline'; " +
            "img-src 'self' data: blob:; " +
            "font-src 'self' data:; " +
            "connect-src 'self'; " +
            "media-src 'self' blob:; " +
            "object-src 'none'; " +
            "frame-ancestors 'none'; " +
            "base-uri 'self'; " +
            "form-action 'self'";
    }

    await next();
});

// OpenAPI-JSON-Endpunkt und Scalar-UI – nur in der Entwicklung. In Produktion wäre das
// eine vollständige, anonym lesbare Beschreibung der gesamten API (Phase 12a).
if (app.Environment.IsDevelopment())
{
    // AllowAnonymous wegen der Fallback-Policy – sonst wäre Scalar nur mit Cookie nutzbar.
    app.MapOpenApi().AllowAnonymous();
    app.MapScalarApiReference(options =>
    {
        options.Title = "HorizonNET API";
    }).AllowAnonymous();
}
else
{
    // HSTS bewusst nur außerhalb der Entwicklung: Der Header gilt lange und würde sonst
    // localhost im Browser dauerhaft auf HTTPS festnageln.
    app.UseHsts();
}

app.UseHttpsRedirection();

// Same-Origin-Hosting: Diese API liefert auch den Blazor-WASM-Client aus (ein Origin für
// App + Daten – deshalb gibt es hier kein CORS). Die App-Hülle ist öffentlich, geschützt
// sind die Daten dahinter. Kein UseBlazorFrameworkFiles: Das ist der Vor-Fingerprinting-Weg
// und kollidiert mit den MapStaticAssets-Endpunkten (Endpoint gesetzt, aber im Branch nie
// ausgeführt → 500 auf _framework-Dateien). MapStaticAssets (unten) bedient alles –
// _framework, gehashte Modul-Namen aus der Importmap, Kompression, Cache-Header.
app.UseStaticFiles();

app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

// Fingerprint-Assets (z. B. js/shortcuts.{hash}.js aus der Importmap): UseStaticFiles
// kennt nur die Originalnamen, erst diese Endpunkte bedienen die gehashten – inklusive
// Brotli-Kompression und Immutable-Cache-Headern. AllowAnonymous wegen der Fallback-Policy.
app.MapStaticAssets().AllowAnonymous();

app.MapControllers();

// Deep-Links und F5 auf Client-Routen (/settings, /journal/…) liefern die App-Hülle;
// AllowAnonymous wegen der Fallback-Policy – der Login findet ja erst IN der App statt.
app.MapFallbackToFile("index.html").AllowAnonymous();

// Version der laufenden API (Phase 9b). Bewusst anonym erreichbar, damit der Client die
// Version auch ohne/vor einem Login prüfen kann (relevant nach Einführung der Auth).
app.MapGet("/api/version", () =>
{
    var asm = typeof(Program).Assembly;
    var version = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
                  ?? asm.GetName().Version?.ToString()
                  ?? "0.0.0";
    // Build-Zeit = Schreibzeit der Assembly; bei Single-File-Publish ggf. nicht vorhanden.
    DateTime? buildUtc = !string.IsNullOrEmpty(asm.Location) && File.Exists(asm.Location)
        ? File.GetLastWriteTimeUtc(asm.Location)
        : null;
    return Results.Ok(new AppVersionDto(version, buildUtc));
}).AllowAnonymous();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

    // SQLite im WAL-Modus betreiben. Im Standardmodus schreibt SQLite direkt in die
    // Hauptdatei und muss Leser so lange aussperren; mit WAL gehen Änderungen zunächst in
    // eine Nebendatei, und Leser sehen weiter einen konsistenten Stand. Damit blockieren
    // sich Hintergrundarbeit (Google-Sync) und UI-Abfragen nicht mehr gegenseitig –
    // die häufigste Ursache für "database is locked".
    //
    // Der Modus steht im Datei-Header und bleibt erhalten; der Aufruf hier stellt nur
    // sicher, dass auch eine frisch angelegte Datenbank ihn bekommt.
    //
    // Fürs Backup relevant: Neben horizonnet.db liegen nun -wal und -shm. Nur die .db zu
    // kopieren reicht nicht mehr – scripts\backup-database.ps1 nimmt beide Dateien mit.
    db.Database.ExecuteSqlRaw("PRAGMA journal_mode=WAL;");

    // Ausstehende Migrationen beim Start automatisch anwenden
    db.Database.Migrate();

    // Benutzer-Seed: Solange kein Konto existiert, wird es aus der Konfiguration angelegt
    // (Auth:Username + Auth:InitialPassword, lokal in appsettings.Secrets.json). Das
    // Passwort wird nur für diesen einen Seed gelesen – existiert das Konto, ist ein
    // späterer Wert in der Konfiguration wirkungslos.
    var userManager = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();
    if (!userManager.Users.Any())
    {
        var username = app.Configuration["Auth:Username"];
        var password = app.Configuration["Auth:InitialPassword"];

        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
        {
            // Kein Abbruch: Die API läuft weiter, nur einloggen kann sich niemand,
            // bis die Werte gesetzt sind und der nächste Start den Seed nachholt.
            app.Logger.LogWarning(
                "Kein Benutzerkonto vorhanden und Auth:Username/Auth:InitialPassword nicht konfiguriert – Login ist bis zum nächsten Start mit gesetzten Werten nicht möglich.");
        }
        else
        {
            var created = await userManager.CreateAsync(new IdentityUser(username), password);
            if (!created.Succeeded)
                throw new InvalidOperationException("Benutzer-Seed fehlgeschlagen: "
                    + string.Join("; ", created.Errors.Select(e => e.Description)));

            app.Logger.LogInformation("Benutzerkonto '{Username}' angelegt.", username);
        }
    }
}

app.Run();
