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

    // Ersatz, falls die Zustände des Projekts nicht gelesen werden können: die
    // abgeschlossenen Zustände der Standardvorlagen (Agile, Scrum, Basic, CMMI).
    private static readonly string[] DefaultClosedStates = ["Closed", "Done", "Removed", "Resolved"];

    // Zustandskategorien, die als "abgeschlossen" gelten. Resolved zählt mit (wie bisher
    // der Zustand "Resolved" in Agile): Das Item ist fachlich erledigt, wartet nur auf Abnahme.
    private static readonly string[] ClosedCategories = ["Completed", "Removed", "Resolved"];

    private static readonly string[] Fields =
    [
        "System.Id", "System.Title", "System.State", "System.WorkItemType",
        "System.TeamProject", "Microsoft.VSTS.Common.Priority", "System.Description",
        // Bugs haben (je nach Prozessvorlage) keine Beschreibung, sondern Reproschritte.
        "Microsoft.VSTS.TCM.ReproSteps"
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

    // Seitenweise über den Continuation-Token: Organisationen mit mehr als 500 Projekten
    // bekämen sonst nur einen Ausschnitt. Die Seitenzahl ist gedeckelt, damit ein Server,
    // der immer wieder einen Token schickt, keine Endlosschleife auslöst.
    private const int MaxProjectPages = 20;

    public async Task<List<string>> GetProjectsAsync()
    {
        var c = await RequireConnectionAsync();
        var names = new List<string>();
        string? continuation = null;

        for (var page = 0; page < MaxProjectPages; page++)
        {
            var url = $"{c.OrganizationUrl}/_apis/projects?$top=500&api-version={ApiVersion}"
                      + (continuation is null ? "" : $"&continuationToken={Uri.EscapeDataString(continuation)}");
            var (doc, next) = await SendWithContinuationAsync(c, HttpMethod.Get, url);
            using (doc)
            {
                names.AddRange(Read(() => doc.RootElement.GetProperty("value").EnumerateArray()
                    .Select(p => GetString(p, "name") ?? "")
                    .Where(n => n.Length > 0)
                    .ToList()));
            }
            if (string.IsNullOrEmpty(next) || next == continuation) break;
            continuation = next;
        }

        return names.Distinct(StringComparer.OrdinalIgnoreCase)
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
        var imported = await tasks.GetAzureDevOpsWorkItemIdsAsync(c.OrganizationUrl);

        // Reihenfolge der WIQL-Abfrage (zuletzt geändert zuerst) beibehalten.
        var byId = items.ToDictionary(i => i.Id);
        return ids.Where(byId.ContainsKey)
            .Select(id => ToDto(c, byId[id], imported.Contains(id)))
            .ToList();
    }

    // Legt für jedes ausgewählte Work Item einen Task im Horizon-Projekt an.
    // Titel & Co. werden frisch aus Azure DevOps gelesen, nicht vom Client übernommen.
    //
    // Alles oder nichts: Die Tasks gehen in einer Transaktion in die Datenbank. Läuft
    // parallel ein zweiter Import derselben Items (Doppelklick, zweiter Tab), schlägt der
    // eindeutige Index zu und dieser Import legt gar nichts an, statt Dubletten zu erzeugen.
    public async Task<(List<TaskItem> Created, int Skipped)> ImportAsync(int projectId, IReadOnlyCollection<int> workItemIds)
    {
        var c = await RequireConnectionAsync();
        var azdoProject = await RequireLinkedProjectAsync(projectId);

        var requested = workItemIds.Distinct().ToList();
        var imported = await tasks.GetAzureDevOpsWorkItemIdsAsync(c.OrganizationUrl);
        var toFetch = requested.Where(id => !imported.Contains(id)).ToHashSet();
        var items = await GetWorkItemsAsync(c, toFetch.ToList());

        var toCreate = new List<TaskItem>();
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
                Description = NullIfEmpty(Truncate(HtmlToText(item.DescriptionHtml ?? item.ReproStepsHtml), MaxDescriptionLength)),
                Link = TaskLink.IsValid(link) ? link : null,
                Priority = MapPriority(item.Priority),
                ProjectId = projectId,
                Status = WorkStatus.Planned,
                Kind = TaskKind.Task,
                AzureDevOpsWorkItemId = item.Id,
                AzureDevOpsOrganizationUrl = c.OrganizationUrl
            };
            toCreate.Add(task);
        }

        if (!await tasks.CreateImportedAsync(toCreate))
            throw new AzureDevOpsException(
                "Einige Work Items wurden gerade parallel importiert. Bitte die Liste neu laden.");

        return (toCreate, requested.Count - toCreate.Count);
    }

    // ── Bausteine ────────────────────────────────────────────────────────────

    internal record WorkItem(int Id, string Title, string State, string Type, string Project, int? Priority,
        string? DescriptionHtml, string? ReproStepsHtml);

    // Offene und abgeschlossene Items getrennt abfragen: In einer gemeinsamen Abfrage
    // füllten bei langen Projekten die (meist zahlreicheren, frischer geänderten)
    // abgeschlossenen Items das $top-Limit, und alte offene Items fielen heraus.
    // Offene stehen deshalb immer vollständig vorn.
    private async Task<List<int>> QueryMyWorkItemIdsAsync(AzureDevOpsConnection c, string azdoProject, bool includeClosed)
    {
        var closedStates = await GetClosedStatesAsync(c, azdoProject);
        var ids = await RunWiqlAsync(c, azdoProject, closedStates, closed: false);
        if (includeClosed)
            ids.AddRange((await RunWiqlAsync(c, azdoProject, closedStates, closed: true)).Where(id => !ids.Contains(id)));
        return ids;
    }

    // Die abgeschlossenen Zustände des Projekts, aus den Zustandskategorien seiner
    // Work-Item-Typen. So stimmt die Trennung auch bei eigenen Prozessvorlagen
    // ("Erledigt", "Abgelehnt" …). Klappt das nicht, gelten die Namen der Standardvorlagen.
    private async Task<IReadOnlyList<string>> GetClosedStatesAsync(AzureDevOpsConnection c, string azdoProject)
    {
        try
        {
            using var doc = await SendAsync(c, HttpMethod.Get,
                $"{c.OrganizationUrl}/{Uri.EscapeDataString(azdoProject)}/_apis/wit/workitemtypes?api-version={ApiVersion}");
            var states = ParseClosedStates(doc.RootElement);
            return states.Count > 0 ? states : DefaultClosedStates;
        }
        catch (AzureDevOpsException)
        {
            return DefaultClosedStates;
        }
    }

    public static List<string> ParseClosedStates(JsonElement root)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("value", out var types)
            || types.ValueKind != JsonValueKind.Array)
            return [];

        foreach (var type in types.EnumerateArray())
        {
            if (type.ValueKind != JsonValueKind.Object || !type.TryGetProperty("states", out var states)
                || states.ValueKind != JsonValueKind.Array)
                continue;
            foreach (var state in states.EnumerateArray())
            {
                if (state.ValueKind != JsonValueKind.Object) continue;
                var name = GetString(state, "name");
                var category = GetString(state, "category");
                if (!string.IsNullOrEmpty(name) && category is not null
                    && ClosedCategories.Contains(category, StringComparer.OrdinalIgnoreCase))
                    result.Add(name);
            }
        }
        return result.Order(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private async Task<List<int>> RunWiqlAsync(AzureDevOpsConnection c, string azdoProject,
        IReadOnlyList<string> closedStates, bool closed)
    {
        // Die Zustandsnamen kommen vom Server, nicht vom Nutzer – trotzdem als WIQL-String
        // escapen (Apostroph verdoppeln). @project bezieht sich auf das Projekt in der URL.
        var states = string.Join(", ", closedStates.Select(s => $"'{s.Replace("'", "''")}'"));
        var where = "[System.TeamProject] = @project AND [System.AssignedTo] = @Me"
                  + $" AND [System.State] {(closed ? "IN" : "NOT IN")} ({states})";
        var wiql = $"SELECT [System.Id] FROM WorkItems WHERE {where} ORDER BY [System.ChangedDate] DESC";

        using var doc = await SendAsync(c, HttpMethod.Post,
            $"{c.OrganizationUrl}/{Uri.EscapeDataString(azdoProject)}/_apis/wit/wiql?$top={MaxItems}&api-version={ApiVersion}",
            new { query = wiql });

        return Read(() => doc.RootElement.GetProperty("workItems").EnumerateArray()
            .Select(w => w.GetProperty("id").GetInt32())
            .ToList());
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

            result.AddRange(Read(() => ParseWorkItems(doc.RootElement)));
        }
        return result;
    }

    private static List<WorkItem> ParseWorkItems(JsonElement root)
    {
        var result = new List<WorkItem>();
        foreach (var w in root.GetProperty("value").EnumerateArray())
        {
            // errorPolicy=omit liefert für nicht (mehr) sichtbare IDs null-Einträge.
            if (w.ValueKind != JsonValueKind.Object || !w.TryGetProperty("fields", out var f)
                || f.ValueKind != JsonValueKind.Object
                || !w.TryGetProperty("id", out var idProp) || !idProp.TryGetInt32(out var id))
                continue;
            result.Add(new WorkItem(
                id,
                GetString(f, "System.Title") ?? "(ohne Titel)",
                GetString(f, "System.State") ?? "",
                GetString(f, "System.WorkItemType") ?? "",
                GetString(f, "System.TeamProject") ?? "",
                f.TryGetProperty("Microsoft.VSTS.Common.Priority", out var p) && p.TryGetInt32(out var pi) ? pi : null,
                NullIfBlank(GetString(f, "System.Description")),
                NullIfBlank(GetString(f, "Microsoft.VSTS.TCM.ReproSteps"))));
        }
        return result;
    }

    private static string? NullIfBlank(string? s) => string.IsNullOrWhiteSpace(s) ? null : s;

    // Liest eine Antwort, deren Form nicht der erwarteten entspricht (fehlende Felder,
    // falscher Typ), als verständlichen Fehler statt als 500er.
    private static T Read<T>(Func<T> read)
    {
        try
        {
            return read();
        }
        catch (Exception ex) when (ex is KeyNotFoundException or InvalidOperationException or FormatException)
        {
            throw new AzureDevOpsException("Azure DevOps hat unerwartet geantwortet.");
        }
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
    private async Task<JsonDocument> SendAsync(AzureDevOpsConnection c, HttpMethod method, string url, object? body = null) =>
        (await SendWithContinuationAsync(c, method, url, body)).Doc;

    // Wie SendAsync, liefert zusätzlich den Continuation-Token der Antwort (Header
    // x-ms-continuationtoken) für seitenweise Abfragen.
    private async Task<(JsonDocument Doc, string? Continuation)> SendWithContinuationAsync(
        AzureDevOpsConnection c, HttpMethod method, string url, object? body = null)
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

            var continuation = response.Headers.TryGetValues("x-ms-continuationtoken", out var values)
                ? values.FirstOrDefault()
                : null;

            await using var stream = await response.Content.ReadAsStreamAsync();
            try
            {
                return (await JsonDocument.ParseAsync(stream), continuation);
            }
            catch (JsonException)
            {
                throw new AzureDevOpsException("Azure DevOps hat unerwartet geantwortet.");
            }
        }
    }
}
