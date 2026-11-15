namespace HorizonNET.Domain.Entities;

// Verbindung zu einer Azure-DevOps-Organisation per Personal Access Token.
// Single-User-App: höchstens eine Zeile (wie GoogleConnection).
public class AzureDevOpsConnection
{
    public int Id { get; set; }

    // Basis-URL der Organisation, normalisiert ohne abschließenden Slash,
    // z. B. https://dev.azure.com/meine-org
    public string OrganizationUrl { get; set; } = string.Empty;

    public string PersonalAccessToken { get; set; } = string.Empty;

    // Anzeigename des Kontos, für "Verbunden als …".
    public string? DisplayName { get; set; }

    public DateTime ConnectedAtUtc { get; set; }
}
