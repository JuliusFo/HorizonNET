namespace HorizonNET.Shared.Transfer.DTOs;

// Status der Azure-DevOps-Verbindung für Einstellungs- und Projektseite.
// Der PAT verlässt den Server nie.
public record AzureDevOpsStatusDto(
    bool Connected,
    string? OrganizationUrl,
    string? DisplayName
);

public record AzureDevOpsConnectDto(string OrganizationUrl, string PersonalAccessToken);

// Ergebnis eines Verbindungsversuchs. Bewusst 200 + Error statt Fehlerstatus: Ein
// falscher PAT ist ein erwartbarer Fall, den die Einstellungsseite inline erklärt –
// der allgemeine Fehler-Toast ("Änderung konnte nicht gespeichert werden") hülfe nicht.
public record AzureDevOpsConnectResultDto(bool Success, string? Error, AzureDevOpsStatusDto? Status);

// Verknüpfung eines Horizon-Projekts mit einem Azure-DevOps-Team-Projekt (null = lösen).
public record AzureDevOpsProjectLinkDto(string? AzureDevOpsProject);

public record AzureDevOpsWorkItemDto(
    int Id,
    string Title,
    string State,
    string Type,
    string Project,
    int? Priority,
    string Url,
    bool AlreadyImported
);

// Liste + optionale Fehlermeldung (abgelaufener PAT, Projekt umbenannt …), damit der
// Import-Dialog den Grund anzeigen kann statt nur "Daten konnten nicht geladen werden".
public record AzureDevOpsWorkItemListDto(List<AzureDevOpsWorkItemDto> Items, string? Error);

public record AzureDevOpsImportDto(int ProjectId, List<int> WorkItemIds);

public record AzureDevOpsImportResultDto(int Imported, int Skipped, string? Error);
