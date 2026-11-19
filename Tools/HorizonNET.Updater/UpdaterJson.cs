using System.Text.Json.Serialization;
using HorizonNET.Shared.Transfer.DTOs;
using HorizonNET.Shared.Transfer.Update;

namespace HorizonNET.Updater;

// Quellgenerierte Serialisierung: schneller Start und bereit fürs Trimmen. Getrimmt wird der
// Updater derzeit nicht (.claude/rules/updater.md).
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    PropertyNameCaseInsensitive = true,
    UseStringEnumConverter = true,
    WriteIndented = true)]
[JsonSerializable(typeof(GitHubRelease))]
[JsonSerializable(typeof(UpdateRunStatus))]
[JsonSerializable(typeof(UpdateJournal))]
[JsonSerializable(typeof(PackageManifest))]
[JsonSerializable(typeof(AppVersionDto))]
internal sealed partial class UpdaterJson : JsonSerializerContext;

// update\journal.json: Was apply schon geändert hat. Liegt es nach einem Absturz noch da, rollt
// der nächste Lauf zuerst zurück.
internal sealed class UpdateJournal
{
    public string TargetVersion { get; set; } = "";
    public string? FromVersion { get; set; }
    public DateTime StartedUtc { get; set; }

    // Starttyp des Dienstes vor dem Update. Während apply steht er auf „Deaktiviert“: Ein Neustart
    // mitten im Tausch darf die neue Version nicht unbeobachtet starten (Migrationen!), bevor die
    // Aufgabe „HorizonNET Recover“ zurückgerollt hat. Das Zurückrollen setzt ihn wieder.
    public ServiceStartType? OriginalStartType { get; set; }

    public bool ServiceStopped { get; set; }
    public string? BackupDirectory { get; set; }

    // Eingetragen, BEVOR der Ordner verschoben wird. HadPrevious: Es gab einen alten Stand, der
    // nach <name>.previous wandert (sonst ist der Ordner neu und fällt beim Zurückrollen weg).
    public List<SwappedComponent> Swapped { get; set; } = [];

    // Gesetzt, bevor der Dienst mit der neuen Version startet: Ab dann können Migrationen gelaufen
    // sein, und das Zurückrollen spielt auch die gesicherte DB zurück.
    public bool NewVersionStarted { get; set; }
}

internal sealed record SwappedComponent(string Name, bool HadPrevious);

// manifest.json im Paket, geschrieben von scripts/deploy-package.ps1.
internal sealed class PackageManifest
{
    public string? Product { get; set; }
    public string? Version { get; set; }
    public string? Commit { get; set; }
    public string? BuiltUtc { get; set; }
    public string? RuntimeId { get; set; }
    public string? Runtime { get; set; }
}
