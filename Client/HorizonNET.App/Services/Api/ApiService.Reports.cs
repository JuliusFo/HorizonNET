using System.Net.Http.Json;
using HorizonNET.Shared.Transfer.DTOs;

namespace HorizonNET.App.Services;

// Auswertungen über Tasks (Wochenabschluss).
public partial class ApiService
{
    public Task<WeeklyReportDto?> GetWeeklyReportAsync(
        DateOnly weekStart, IEnumerable<int> workspaceIds, bool withoutWorkspace)
    {
        var parameters = new List<string> { $"weekStart={weekStart:yyyy-MM-dd}" };
        parameters.AddRange(workspaceIds.Select(id => $"workspaceIds={id}"));
        if (withoutWorkspace) parameters.Add("withoutWorkspace=true");
        return http.GetFromJsonAsync<WeeklyReportDto>($"api/reports/weekly?{string.Join('&', parameters)}");
    }
}
