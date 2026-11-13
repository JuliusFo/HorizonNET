using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using HorizonNET.Domain.Entities;
using HorizonNET.Domain.Interfaces;
using HorizonNET.Shared.Transfer;
using HorizonNET.Shared.Transfer.DTOs;
using HorizonNET.Shared.Transfer.Enums;

namespace HorizonNET.Api.Services;

// Fehler, die der Nutzer verstehen und beheben kann (falscher PAT, Projekt fehlt …).
// Die Meldung ist für die Anzeige gedacht.
public class AzureDevOpsException(string message) : Exception(message);

// Zugriff auf die Azure-DevOps-REST-API per Personal Access Token.
//
// Bewusst ohne das Microsoft.TeamFoundationServer.Client-Paket: Gebraucht werden vier
// Aufrufe (Verbindungstest, Projektliste, WIQL, Work Items lesen), dafür lohnt die
// schwere Abhängigkeit nicht.
public partial class AzureDevOpsService(
    HttpClient http,
    IAzureDevOpsConnectionRepository connections,
    ITaskRepository tasks,
    IProjectRepository projects)
{
    private const string ApiVersion = "7.1";

    // Zustände, die als "abgeschlossen" gelten (Agile, Scrum, Basic, CMMI). Ohne
    // includeClosed fragt der Import sie gar nicht erst ab – sonst kämen bei langen
    // Projekten hunderte erledigte Items zurück.
    private static readonly string[] ClosedStates = ["Closed", "Done", "Removed", "Resolved"];

    private static readonly string[] Fields =
    [
        "System.Id", "System.Title", "System.State", "System.WorkItemType",
        "System.TeamProject", "Microsoft.VSTS.Common.Priority", "System.Description"
    ];

    // Grenzen der Task-Spalten (AppDbContext).
    private const int MaxTitleLength = 300;
    private const int MaxDescriptionLength = 2000;

    // Obergrenze je Abfrage. Wer mehr als 200 offene Items hat, sieht die zuletzt geänderten.
    private const int MaxItems = 200;

    // ── Verbindung ──────────────────────────────────────────────────────────

    public async Task<AzureDevOpsStatusDto> GetStatusAsync()
    {
        var c = await connections.GetAsync();
        return c is null
            ? new AzureDevOpsStatusDto(false, null, null)
            : new AzureDevOpsStatusDto(true, c.OrganizationUrl, c.DisplayName);
    }

    // Prüft URL und PAT mit einem echten Aufruf und speichert erst dann.
    public async Task<AzureDevOpsStatusDto> ConnectAsync(string organizationUrl, string pat)
    {
        var orgUrl = NormalizeOrganizationUrl(organizationUrl)
            ?? throw new AzureDevOpsException(
                "Ungültige Organisations-URL. Erwartet z. B. https://dev.azure.com/meine-organisation");
        pat = pat.Trim();
        if (pat.Length == 0) throw new AzureDevOpsException("Bitte einen Personal Access Token angeben.");

        var connection = new AzureDevOpsConnection
        {
            OrganizationUrl = orgUrl,
            PersonalAccessToken = pat,
            ConnectedAtUtc = DateTime.UtcNow
        };

        using var doc = await SendAsync(connection, HttpMethod.Get, $"{orgUrl}/_apis/connectionData");
        var user = doc.RootElement.TryGetProperty("authenticatedUser", out var u) ? u : default;
        connection.DisplayName = user.ValueKind == JsonValueKind.Object
            && user.TryGetProperty("providerDisplayName", out var name)
            ? name.GetString()
            : null;

        await connections.SaveAsync(connection);
        return new AzureDevOpsStatusDto(true, connection.OrganizationUrl, connection.DisplayName);
    }

    public Task DisconnectAsync() => connections.DeleteAsync();

    // Erlaubt nur https; schneidet Pfade nach der Organisation ab, damit ein aus dem
    // Browser kopierter Link (…/meine-org/MeinProjekt/_workitems) auch funktioniert.
    // Unterstützt dev.azure.com/{org} und das alte {org}.visualstudio.com.
    public static string? NormalizeOrganizationUrl(string? input)
    {
        if (string.IsNullOrWhiteSpace(input)) return null;
        var text = input.Trim();
        if (!text.Contains("://")) text = "https://" + text;
        if (!Uri.TryCreate(text, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
            return null;

        var segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (uri.Host.Equals("dev.azure.com", StringComparison.OrdinalIgnoreCase))
        {
            if (segments.Length == 0) return null;
            return $"https://dev.azure.com/{segments[0]}";
        }
        if (uri.Host.EndsWith(".visualstudio.com", StringComparison.OrdinalIgnoreCase))
            return $"https://{uri.Host}";

        // Azure DevOps Server (on-prem): https://server/tfs/Collection – Pfad bis zur
        // Collection übernehmen. Ohne Pfad wäre die Collection unbekannt.
        if (segments.Length == 0) return null;
        var take = segments[0].Equals("tfs", StringComparison.OrdinalIgnoreCase) && segments.Length > 1 ? 2 : 1;
        return $"https://{uri.Authority}/{string.Join('/', segments.Take(take))}";
    }

    // ── Projekte ─────────────────────────────────────────────────────────────

    public async Task<List<string>> GetProjectsAsync()
    {
        var c = await RequireConnectionAsync();
        using var doc = await SendAsync(c, HttpMethod.Get,
            $"{c.OrganizationUrl}/_apis/projects?$top=500&api-version={ApiVersion}");

        return doc.RootElement.GetProperty("value").EnumerateArray()
            .Select(p => p.GetProperty("name").GetString() ?? "")
            .Where(n => n.Length > 0)
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    // ── Work Items ───────────────────────────────────────────────────────────

    // Alle dem angemeldeten Konto zugewiesenen Work Items im verknüpften AzDO-Projekt.
    // Die Status-Filterung (Neu/Aktiv/…) macht der Client – die Zustände unterscheiden
    // sich je Prozessvorlage, und so bietet der Dialog genau die an, die vorkommen.
    public async Task<List<AzureDevOpsWorkItemDto>> GetMyWorkItemsAsync(int projectId, bool includeClosed)
    {
        var c = await RequireConnectionAsync();
        var azdoProject = await RequireLinkedProjectAsync(projectId);

        var ids = await QueryMyWorkItemIdsAsync(c, azdoProject, includeClosed);
        var items = await GetWorkItemsAsync(c, ids);
        var imported = await tasks.GetAzureDevOpsWorkItemIdsAsync();

        // Reihenfolge der WIQL-Abfrage (zuletzt geändert zuerst) beibehalten.
        var byId = items.ToDictionary(i => i.Id);
        return ids.Where(byId.ContainsKey)
            .Select(id => ToDto(c, byId[id], imported.Contains(id)))
            .ToList();
    }

    // Legt für jedes ausgewählte Work Item einen Task im Horizon-Projekt an.
    // Titel & Co. werden frisch aus Azure DevOps gelesen, nicht vom Client übernommen.
    public async Task<(List<TaskItem> Created, int Skipped)> ImportAsync(int projectId, IReadOnlyCollection<int> workItemIds)
    {
        var c = await RequireConnectionAsync();
        var azdoProject = await RequireLinkedProjectAsync(projectId);

        var requested = workItemIds.Distinct().ToList();
        var imported = await tasks.GetAzureDevOpsWorkItemIdsAsync();
        var toFetch = requested.Where(id => !imported.Contains(id)).ToHashSet();
        var items = await GetWorkItemsAsync(c, toFetch.ToList());

        var created = new List<TaskItem>();
        foreach (var item in items)
        {
            // Nur Items aus dem verknüpften Projekt – der Dialog zeigt nichts anderes,
            // also ist alles andere ein manipulierter oder veralteter Aufruf.
            if (!string.Equals(item.Project, azdoProject, StringComparison.OrdinalIgnoreCase)) continue;
            // Nur angefragte und noch nicht importierte IDs; Remove schützt auch vor
            // doppelten Einträgen in der Antwort.
            if (!toFetch.Remove(item.Id)) continue;

            var link = WebUrl(c, item);
            var task = new TaskItem
            {
                Title = Truncate($"#{item.Id} {item.Title}", MaxTitleLength),
                Description = NullIfEmpty(Truncate(HtmlToText(item.DescriptionHtml), MaxDescriptionLength)),
                Link = TaskLink.IsValid(link) ? link : null,
                Priority = MapPriority(item.Priority),
                ProjectId = projectId,
                Status = WorkStatus.Planned,
                Kind = TaskKind.Task,
                AzureDevOpsWorkItemId = item.Id
            };
            created.Add(await tasks.CreateAsync(task));
        }

        return (created, requested.Count - created.Count);
    }

    // ── Bausteine ────────────────────────────────────────────────────────────

    internal record WorkItem(int Id, string Title, string State, string Type, string Project, int? Priority, string? DescriptionHtml);

    // Offene und abgeschlossene Items getrennt abfragen: In einer gemeinsamen Abfrage
    // füllten bei langen Projekten die (meist zahlreicheren, frischer geänderten)
    // abgeschlossenen Items das $top-Limit, und alte offene Items fielen heraus.
    // Offene stehen deshalb immer vollständig vorn.
    private async Task<List<int>> QueryMyWorkItemIdsAsync(AzureDevOpsConnection c, string azdoProject, bool includeClosed)
    {
        var ids = await RunWiqlAsync(c, azdoProject, closed: false);
        if (includeClosed)
            ids.AddRange((await RunWiqlAsync(c, azdoProject, closed: true)).Where(id => !ids.Contains(id)));
        return ids;
    }

    private async Task<List<int>> RunWiqlAsync(AzureDevOpsConnection c, string azdoProject, bool closed)
    {
        // Feste Zustandsnamen, kein Nutzertext – nichts zu escapen. @project bezieht sich
        // auf das Projekt in der URL.
        var states = string.Join(", ", ClosedStates.Select(s => $"'{s}'"));
        var where = "[System.TeamProject] = @project AND [System.AssignedTo] = @Me"
                  + $" AND [System.State] {(closed ? "IN" : "NOT IN")} ({states})";
        var wiql = $"SELECT [System.Id] FROM WorkItems WHERE {where} ORDER BY [System.ChangedDate] DESC";

        using var doc = await SendAsync(c, HttpMethod.Post,
            $"{c.OrganizationUrl}/{Uri.EscapeDataString(azdoProject)}/_apis/wit/wiql?$top={MaxItems}&api-version={ApiVersion}",
            new { query = wiql });

        return doc.RootElement.GetProperty("workItems").EnumerateArray()
            .Select(w => w.GetProperty("id").GetInt32())
            .ToList();
    }

    private async Task<List<WorkItem>> GetWorkItemsAsync(AzureDevOpsConnection c, List<int> ids)
    {
        var result = new List<WorkItem>();
        // Die API nimmt höchstens 200 IDs je Aufruf.
        foreach (var chunk in ids.Chunk(200))
        {
            var url = $"{c.OrganizationUrl}/_apis/wit/workitems?ids={string.Join(',', chunk)}"
                    + $"&fields={string.Join(',', Fields)}&errorPolicy=omit&api-version={ApiVersion}";
            using var doc = await SendAsync(c, HttpMethod.Get, url);

            foreach (var w in doc.RootElement.GetProperty("value").EnumerateArray())
            {
                // errorPolicy=omit liefert für nicht (mehr) sichtbare IDs null-Einträge.
                if (w.ValueKind != JsonValueKind.Object || !w.TryGetProperty("fields", out var f)) continue;
                result.Add(new WorkItem(
                    w.GetProperty("id").GetInt32(),
                    GetString(f, "System.Title") ?? "(ohne Titel)",
                    GetString(f, "System.State") ?? "",
                    GetString(f, "System.WorkItemType") ?? "",
                    GetString(f, "System.TeamProject") ?? "",
                    f.TryGetProperty("Microsoft.VSTS.Common.Priority", out var p) && p.TryGetInt32(out var pi) ? pi : null,
                    GetString(f, "System.Description")));
            }
        }
        return result;
    }

    private static string? GetString(JsonElement fields, string name) =>
        fields.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static AzureDevOpsWorkItemDto ToDto(AzureDevOpsConnection c, WorkItem w, bool alreadyImported) =>
        new(w.Id, w.Title, w.State, w.Type, w.Project, w.Priority, WebUrl(c, w), alreadyImported);

    internal static string WebUrl(AzureDevOpsConnection c, WorkItem w) =>
        $"{c.OrganizationUrl}/{Uri.EscapeDataString(w.Project)}/_workitems/edit/{w.Id}";

    // AzDO-Priorität 1 (höchste) … 4 (niedrigste) auf die drei Horizon-Stufen.
    public static Priority MapPriority(int? azdoPriority) => azdoPriority switch
    {
        1 => Priority.High,
        3 or 4 => Priority.Low,
        _ => Priority.Medium
    };

    // Die Beschreibung ist HTML; Tasks kennen nur Klartext.
    public static string HtmlToText(string? html)
    {
        if (string.IsNullOrWhiteSpace(html)) return "";
        var text = BlockEndRegex().Replace(html, "\n");
        text = TagRegex().Replace(text, "");
        text = WebUtility.HtmlDecode(text).Replace(" ", " ").Replace("\r", "");
        text = string.Join('\n', text.Split('\n').Select(l => l.TrimEnd()));
        return ManyNewlinesRegex().Replace(text, "\n\n").Trim();
    }

    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..(max - 1)] + "…";

    private static string? NullIfEmpty(string s) => s.Length == 0 ? null : s;

    [GeneratedRegex(@"<br\s*/?>|</(p|div|li|h[1-6]|tr)>", RegexOptions.IgnoreCase)]
    private static partial Regex BlockEndRegex();

    [GeneratedRegex("<[^>]+>")]
    private static partial Regex TagRegex();

    [GeneratedRegex(@"\n{3,}")]
    private static partial Regex ManyNewlinesRegex();

    private async Task<AzureDevOpsConnection> RequireConnectionAsync() =>
        await connections.GetAsync()
        ?? throw new AzureDevOpsException("Azure DevOps ist nicht verbunden (Einstellungen → Azure DevOps).");

    private async Task<string> RequireLinkedProjectAsync(int projectId)
    {
        var project = await projects.GetByIdAsync(projectId)
            ?? throw new AzureDevOpsException("Projekt nicht gefunden.");
        return project.AzureDevOpsProject
            ?? throw new AzureDevOpsException("Dieses Projekt ist mit keinem Azure-DevOps-Projekt verknüpft.");
    }

    // Ein Aufruf mit Basic-Auth (leerer Benutzer + PAT) und Übersetzung der typischen
    // Fehlerbilder in verständliche Meldungen.
    private async Task<JsonDocument> SendAsync(AzureDevOpsConnection c, HttpMethod method, string url, object? body = null)
    {
        using var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic",
            Convert.ToBase64String(Encoding.ASCII.GetBytes(":" + c.PersonalAccessToken)));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        if (body is not null) request.Content = JsonContent.Create(body);

        HttpResponseMessage response;
        try
        {
            response = await http.SendAsync(request);
        }
        catch (HttpRequestException)
        {
            throw new AzureDevOpsException("Azure DevOps ist nicht erreichbar. Stimmt die Organisations-URL?");
        }
        catch (TaskCanceledException)
        {
            throw new AzureDevOpsException("Azure DevOps hat nicht rechtzeitig geantwortet.");
        }

        using (response)
        {
            // Ein ungültiger PAT liefert je nach Endpunkt 401 oder 203 mit einer
            // HTML-Anmeldeseite – beides heißt "nicht angemeldet".
            var isJson = response.Content.Headers.ContentType?.MediaType?.Contains("json") == true;
            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.NonAuthoritativeInformation
                || (response.IsSuccessStatusCode && !isJson))
                throw new AzureDevOpsException(
                    "Anmeldung bei Azure DevOps fehlgeschlagen – Token ungültig oder abgelaufen.");
            if (response.StatusCode == HttpStatusCode.Forbidden)
                throw new AzureDevOpsException(
                    "Keine Berechtigung. Der Token braucht den Scope „Work Items (Read)“.");
            if (response.StatusCode == HttpStatusCode.NotFound)
                throw new AzureDevOpsException("Organisation oder Projekt in Azure DevOps nicht gefunden.");
            if (!response.IsSuccessStatusCode)
                throw new AzureDevOpsException($"Azure DevOps meldet einen Fehler ({(int)response.StatusCode}).");

            await using var stream = await response.Content.ReadAsStreamAsync();
            return await JsonDocument.ParseAsync(stream);
        }
    }
}
