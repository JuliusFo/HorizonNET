using System.Net.Http.Json;
using System.Text.Json;
using HorizonNET.Shared.Transfer.DTOs;
using HorizonNET.Shared.Transfer.Update;

namespace HorizonNET.App.Services;

// Auto-Update (ADR 0007): Stand, „Jetzt prüfen“, Installation anfordern.
public partial class ApiService
{
    public Task<UpdateInfoDto?> GetUpdateInfoAsync() =>
        http.GetFromJsonAsync<UpdateInfoDto>("api/update");

    public Task<UpdateInfoDto?> CheckForUpdateAsync() =>
        PostAsync<UpdateInfoDto>("api/update/check");

    // Leise: Die Seite zeigt die Meldung des Servers (409 „läuft bereits“ usw.) selbst.
    public async Task<(UpdateInfoDto? Info, string? Error)> RequestUpdateInstallAsync()
    {
        using var request = ApiErrorHandler.QuietRequest(HttpMethod.Post, "api/update/install");
        using var response = await http.SendAsync(request);
        if (response.IsSuccessStatusCode)
            return (await response.Content.ReadFromJsonAsync<UpdateInfoDto>(), null);

        string? detail = null;
        try
        {
            detail = (await response.Content.ReadFromJsonAsync<JsonElement>()).TryGetProperty("detail", out var d)
                ? d.GetString()
                : null;
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException)
        {
            // Kein ProblemDetails (z. B. Server nicht erreichbar).
        }
        return (null, detail ?? "Das Update ließ sich nicht anfordern.");
    }

    // Während eines Updates ist die API kurz weg: leise abfragen, Fehler → null.
    public async Task<AppVersionDto?> GetApiVersionQuietAsync()
    {
        using var request = ApiErrorHandler.QuietRequest(HttpMethod.Get, "api/version");
        using var response = await http.SendAsync(request);
        return await response.Content.ReadFromJsonAsync<AppVersionDto>();
    }

    public async Task<UpdateInfoDto?> GetUpdateInfoQuietAsync()
    {
        using var request = ApiErrorHandler.QuietRequest(HttpMethod.Get, "api/update");
        using var response = await http.SendAsync(request);
        return await response.Content.ReadFromJsonAsync<UpdateInfoDto>();
    }
}
