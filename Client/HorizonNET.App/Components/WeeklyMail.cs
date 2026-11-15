using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using HorizonNET.Shared.Transfer.DTOs;
using HorizonNET.Shared.Transfer.Enums;

namespace HorizonNET.App.Components;

// Baut aus dem Wochenbericht Betreff, Text und den mailto:-Link der Wochenabschluss-Mail.
// Reine Textlogik ohne DOM – deshalb hier und nicht in der Seite, und unit-getestet.
public static partial class WeeklyMail
{
    // Grenze, ab der Mail-Programme (v. a. Outlook unter Windows) den mailto:-Link
    // abschneiden oder gar nicht öffnen. Die Seite warnt darüber und bietet Kopieren an.
    public const int SafeMailtoLength = 2000;

    private static readonly CultureInfo De = CultureInfo.GetCultureInfo("de-DE");

    public static DateOnly MondayOf(DateOnly date) =>
        date.AddDays(-(((int)date.DayOfWeek + 6) % 7));

    public static int WeekNumber(DateOnly monday) => ISOWeek.GetWeekOfYear(monday.ToDateTime(TimeOnly.MinValue));

    public static int WeekYear(DateOnly monday) => ISOWeek.GetYear(monday.ToDateTime(TimeOnly.MinValue));

    public static string Subject(DateOnly monday) =>
        $"Wochenabschluss KW {WeekNumber(monday)}/{WeekYear(monday)}";

    // Montag bis Freitag – der Bericht zählt zwar das Wochenende mit, gemeint ist aber
    // die Arbeitswoche.
    public static string RangeLabel(DateOnly monday) =>
        $"{monday.ToString("dd.MM.", De)}–{monday.AddDays(4).ToString("dd.MM.yyyy", De)}";

    public static string Body(WeeklyReportDto report, string? greeting, string? signature)
    {
        var sb = new StringBuilder();
        var hasGreeting = !string.IsNullOrWhiteSpace(greeting);
        if (hasGreeting)
            sb.AppendLine(greeting!.Trim()).AppendLine();

        // Nach "Hallo zusammen," geht es klein weiter, ohne Anrede groß.
        sb.AppendLine($"{(hasGreeting ? "hier" : "Hier")} mein Wochenabschluss für KW {WeekNumber(report.WeekStart)} ({RangeLabel(report.WeekStart)}).");

        var completed = report.Completed
            .OrderBy(t => t.ProjectName is null)
            .ThenBy(t => t.ProjectName, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(t => t.CompletedAt);
        Section(sb, "Erledigt", completed, includeWaitingFor: false);

        Section(sb, "In Arbeit", Sorted(report.Open.Where(t => t.Status == WorkStatus.InProgress)), includeWaitingFor: true);
        Section(sb, "Pausiert / wartet", Sorted(report.Open.Where(t => t.Status == WorkStatus.Paused)), includeWaitingFor: true);
        Section(sb, "Als Nächstes",
            Sorted(report.Open.Where(t => t.Status is WorkStatus.PlannedToday or WorkStatus.PlannedPriority)),
            includeWaitingFor: true);
        Section(sb, "Weitere geplante Aufgaben", Sorted(report.Open.Where(t => t.Status == WorkStatus.Planned)), includeWaitingFor: true);

        if (report.Completed.Count == 0 && report.Open.Count == 0)
            sb.AppendLine().AppendLine("(Keine Aufgaben im gewählten Bereich.)");

        if (!string.IsNullOrWhiteSpace(signature))
            sb.AppendLine().AppendLine(signature.TrimEnd());

        return sb.ToString().TrimEnd();
    }

    private static IEnumerable<WeeklyReportTaskDto> Sorted(IEnumerable<WeeklyReportTaskDto> tasks) =>
        tasks.OrderBy(t => t.ProjectName is null)
             .ThenBy(t => t.ProjectName, StringComparer.CurrentCultureIgnoreCase)
             .ThenByDescending(t => t.Priority)
             .ThenBy(t => t.DueDate ?? DateTime.MaxValue)
             .ThenBy(t => t.Title, StringComparer.CurrentCultureIgnoreCase);

    private static void Section(StringBuilder sb, string heading, IEnumerable<WeeklyReportTaskDto> tasks, bool includeWaitingFor)
    {
        var list = tasks.ToList();
        if (list.Count == 0) return;

        sb.AppendLine().AppendLine($"{heading}:");
        foreach (var t in list)
            sb.AppendLine($"- {Line(t, includeWaitingFor)}");
    }

    private static string Line(WeeklyReportTaskDto t, bool includeWaitingFor)
    {
        var title = t.ParentTitle is null ? t.Title : $"{t.ParentTitle} › {t.Title}";
        var line = t.ProjectName is null ? title : $"{t.ProjectName}: {title}";

        var extras = new List<string>();
        if (includeWaitingFor && !string.IsNullOrWhiteSpace(t.WaitingFor))
            extras.Add($"wartet auf: {t.WaitingFor.Trim()}");
        if (t.CompletedAt is null && t.DueDate is DateTime due)
            extras.Add($"fällig {due.ToString("dd.MM.", De)}");

        return extras.Count == 0 ? line : $"{line} ({string.Join(", ", extras)})";
    }

    // ── Empfänger ────────────────────────────────────────────────────────────────

    // Trennt per Komma, Semikolon oder Leerraum – so wie man Adressen aus Outlook kopiert.
    public static List<string> ParseAddresses(string? input) =>
        (input ?? string.Empty)
            .Split([',', ';', ' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

    public static bool IsValidAddress(string address) => AddressPattern().IsMatch(address);

    [GeneratedRegex(@"^[^@\s,;?&#""<>]+@[^@\s,;?&#""<>]+\.[^@\s,;?&#""<>]+$")]
    private static partial Regex AddressPattern();

    // RFC 6068: Adressen kommagetrennt im Pfad, cc/subject/body als Query. Zeilenumbrüche
    // müssen CRLF sein (%0D%0A), sonst kleben manche Programme alles in eine Zeile.
    public static string MailtoUri(IEnumerable<string> to, IEnumerable<string> cc, string subject, string body)
    {
        var sb = new StringBuilder("mailto:");
        sb.Append(string.Join(',', to.Select(EscapeAddress)));

        var query = new List<string>();
        var ccList = cc.ToList();
        if (ccList.Count > 0) query.Add($"cc={string.Join(',', ccList.Select(EscapeAddress))}");
        query.Add($"subject={Uri.EscapeDataString(subject)}");
        var crlf = body.Replace("\r\n", "\n").Replace("\n", "\r\n");
        query.Add($"body={Uri.EscapeDataString(crlf)}");

        return sb.Append('?').Append(string.Join('&', query)).ToString();
    }

    // Adressen kodieren (RFC 6068): Ein "?", "&" oder "%" in einer Eingabe soll den Link
    // nicht aufbrechen oder weitere Felder einschleusen. Das "@" bleibt lesbar.
    private static string EscapeAddress(string address) =>
        Uri.EscapeDataString(address).Replace("%40", "@");
}
