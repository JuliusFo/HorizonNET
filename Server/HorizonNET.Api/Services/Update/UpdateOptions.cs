using System.Reflection;
using HorizonNET.Shared.Transfer.Update;

namespace HorizonNET.Api.Services.Update;

// Abschnitt "Update" der Konfiguration (ADR 0007). Auf dem vServer setzt server-install.ps1
// Update__Mode=Install, Update__Directory und Update__TaskName; docker-compose.yml Notify.
public sealed class UpdateOptions
{
    public UpdateMode Mode { get; set; } = UpdateMode.Off;

    // <Root>\update: status.json (vom Updater), request\request.json (von hier).
    public string? Directory { get; set; }

    public string TaskName { get; set; } = "HorizonNET Update";

    public TimeSpan InitialDelay { get; set; } = TimeSpan.FromMinutes(2);
    public TimeSpan Interval { get; set; } = TimeSpan.FromHours(6);
}

// Version der laufenden API, einmal gelesen.
public sealed record AppVersionInfo(string Informational, Version? Version)
{
    public static AppVersionInfo FromAssembly(Assembly assembly)
    {
        var informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
                            ?? assembly.GetName().Version?.ToString()
                            ?? "0.0.0";
        return new AppVersionInfo(informational,
            UpdatePackage.TryParseInformational(informational, out var version) ? version : null);
    }
}
