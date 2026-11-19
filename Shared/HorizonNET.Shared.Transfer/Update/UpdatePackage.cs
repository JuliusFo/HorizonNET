using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace HorizonNET.Shared.Transfer.Update;

// Regeln für das Update-Paket am GitHub-Release (ADR 0007). Gemeinsam für die API, die nach neuen
// Versionen sieht, und den Updater, der installiert: Beide müssen dasselbe Release für „das
// neueste mit Paket“ halten.
//
// Repo und Download-Adresse sind fest eingebaut. Der Updater nimmt keine URL von außen, auch
// nicht aus der Antwort der GitHub-API (browser_download_url): Er baut sie aus Tag und Dateiname.
public static partial class UpdatePackage
{
    public const string Repository = "JuliusFo/HorizonNET";
    public const string RuntimeId = "win-x64";
    public const string UserAgent = "HorizonNET-Update";

    public static Uri LatestReleaseApi { get; } = new($"https://api.github.com/repos/{Repository}/releases/latest");

    public static string ZipName(Version version) => $"HorizonNET-{Format(version)}-{RuntimeId}.zip";

    public static string ChecksumName(Version version) => ZipName(version) + ".sha256";

    public static Uri DownloadUri(Version version, string fileName) =>
        new($"https://github.com/{Repository}/releases/download/v{Format(version)}/{Uri.EscapeDataString(fileName)}");

    public static string Format(Version version) => $"{version.Major}.{version.Minor}.{version.Build}";

    // Nur vX.Y.Z – keine Vorabversionen, keine Zusätze.
    public static bool TryParseTag(string? tag, out Version version)
    {
        version = new Version(0, 0, 0);
        if (tag is null) return false;
        var match = TagPattern().Match(tag);
        if (!match.Success) return false;
        version = new Version(int.Parse(match.Groups[1].ValueSpan), int.Parse(match.Groups[2].ValueSpan),
            int.Parse(match.Groups[3].ValueSpan));
        return true;
    }

    // InformationalVersion der laufenden API: "X.Y.Z" oder "X.Y.Z+<commit>".
    public static bool TryParseInformational(string? informational, out Version version)
    {
        var plain = informational?.Split('+', 2)[0];
        return TryParseTag(plain is null ? null : "v" + plain, out version);
    }

    // Das Release zählt nur, wenn es veröffentlicht ist (kein Entwurf, keine Vorabversion), sein
    // Tag vX.Y.Z lautet und ZIP und Prüfsumme vollständig hochgeladen sind. Der Release-Workflow
    // lädt die Prüfsumme zuletzt hoch; solange sie fehlt, gibt es für dieses Release kein Update.
    public static ReleaseCandidate? Evaluate(GitHubRelease? release)
    {
        if (release is null || release.Draft || release.Prerelease) return null;
        if (!TryParseTag(release.TagName, out var version)) return null;

        var zipName = ZipName(version);
        var zip = release.Assets?.FirstOrDefault(a => a.Name == zipName && a.State == "uploaded");
        var sha = release.Assets?.FirstOrDefault(a => a.Name == zipName + ".sha256" && a.State == "uploaded");
        if (zip is null || sha is null) return null;

        return new ReleaseCandidate(version, release.TagName!, zip.Size, release.Body ?? "",
            release.PublishedAt, release.HtmlUrl);
    }

    [GeneratedRegex(@"^v(\d{1,5})\.(\d{1,5})\.(\d{1,5})$")]
    private static partial Regex TagPattern();
}

public record ReleaseCandidate(Version Version, string Tag, long ZipSize, string Notes,
    DateTimeOffset? PublishedAt, string? HtmlUrl);

// Ausschnitt aus GET /repos/{owner}/{repo}/releases/latest.
public sealed class GitHubRelease
{
    [JsonPropertyName("tag_name")] public string? TagName { get; set; }
    [JsonPropertyName("draft")] public bool Draft { get; set; }
    [JsonPropertyName("prerelease")] public bool Prerelease { get; set; }
    [JsonPropertyName("body")] public string? Body { get; set; }
    [JsonPropertyName("published_at")] public DateTimeOffset? PublishedAt { get; set; }
    [JsonPropertyName("html_url")] public string? HtmlUrl { get; set; }
    [JsonPropertyName("assets")] public List<GitHubAsset>? Assets { get; set; }
}

public sealed class GitHubAsset
{
    [JsonPropertyName("name")] public string? Name { get; set; }
    [JsonPropertyName("size")] public long Size { get; set; }
    [JsonPropertyName("state")] public string? State { get; set; }
}
