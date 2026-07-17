using System.Reflection;
using HorizonNET.Api.Services;
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

// EF Core mit SQLite – Datenbankpfad aus appsettings.json
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("DefaultConnection")));

// Repository-Pattern per Dependency Injection registrieren
builder.Services.AddScoped<IWorkspaceRepository, WorkspaceRepository>();
builder.Services.AddScoped<IProjectRepository, ProjectRepository>();
builder.Services.AddScoped<ITaskRepository, TaskRepository>();
builder.Services.AddScoped<IGoogleConnectionRepository, GoogleConnectionRepository>();
builder.Services.AddScoped<INoteRepository, NoteRepository>();
builder.Services.AddScoped<IDailyTaskRepository, DailyTaskRepository>();
builder.Services.AddScoped<ITaskTemplateRepository, TaskTemplateRepository>();
builder.Services.AddScoped<ITimeEntryRepository, TimeEntryRepository>();
builder.Services.AddScoped<IExerciseRepository, ExerciseRepository>();
builder.Services.AddScoped<IExerciseSetRepository, ExerciseSetRepository>();
builder.Services.AddScoped<IBodyWeightRepository, BodyWeightRepository>();

// Google-Kalender-Anbindung (OAuth + späterer Calendar-Zugriff)
builder.Services.AddScoped<GoogleCalendarService>();

var app = builder.Build();

// OpenAPI-JSON-Endpunkt und Scalar-UI bereitstellen
app.MapOpenApi();
app.MapScalarApiReference(options =>
{
    options.Title = "HorizonNET API";
});

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

// Ausstehende Migrationen beim Start automatisch anwenden
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.Migrate();
}

app.Run();
