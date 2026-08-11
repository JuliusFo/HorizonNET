using System.Reflection;
using HorizonNET.Api.Services;
using Microsoft.AspNetCore.DataProtection;
using HorizonNET.Data;
using HorizonNET.Data.Repositories;
using HorizonNET.Domain.Interfaces;
using HorizonNET.Shared.Transfer.DTOs;
using Microsoft.EntityFrameworkCore;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

// Lokale Secrets (Google-Credentials etc.) – Datei ist per .gitignore ausgeschlossen
builder.Configuration.AddJsonFile("appsettings.Secrets.json", optional: true, reloadOnChange: true);

// Controller und Validierung
builder.Services.AddControllers();

// CORS: Blazor-Client darf Anfragen an diese API stellen
builder.Services.AddCors(options => options.AddDefaultPolicy(policy =>
    policy.WithOrigins(
            builder.Configuration["Cors:AllowedOrigin"]
                ?? throw new InvalidOperationException("Cors:AllowedOrigin ist nicht konfiguriert."))
          .AllowAnyHeader()
          .AllowAnyMethod()));

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

var app = builder.Build();

// OpenAPI-JSON-Endpunkt und Scalar-UI – nur in der Entwicklung. In Produktion wäre das
// eine vollständige, anonym lesbare Beschreibung der gesamten API (Phase 12a).
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference(options =>
    {
        options.Title = "HorizonNET API";
    });
}
else
{
    // HSTS bewusst nur außerhalb der Entwicklung: Der Header gilt lange und würde sonst
    // localhost im Browser dauerhaft auf HTTPS festnageln.
    app.UseHsts();
}

app.UseCors();
app.UseHttpsRedirection();
app.UseAuthorization();
app.MapControllers();

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
});

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
}

app.Run();
