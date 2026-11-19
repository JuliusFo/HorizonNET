using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;
using System.ServiceProcess;
using System.Text.Json;
using HorizonNET.Shared.Transfer.Update;
using Microsoft.Win32;

namespace HorizonNET.Updater;

[SupportedOSPlatform("windows")]
internal sealed class WindowsServiceHost(string serviceName) : IServiceHost
{
    public void Stop(TimeSpan timeout)
    {
        using var controller = new ServiceController(serviceName);
        var deadline = DateTime.UtcNow + timeout;
        while (true)
        {
            controller.Refresh();
            switch (controller.Status)
            {
                case ServiceControllerStatus.Stopped:
                    return;
                case ServiceControllerStatus.Running or ServiceControllerStatus.Paused:
                    try
                    {
                        controller.Stop();
                    }
                    catch (InvalidOperationException) when (DateTime.UtcNow < deadline)
                    {
                        // Zustand hat sich seit Refresh geändert (etwa abgestürzt): neu lesen.
                        Thread.Sleep(500);
                        break;
                    }
                    controller.WaitForStatus(ServiceControllerStatus.Stopped, Remaining(deadline));
                    return;
                case ServiceControllerStatus.StopPending:
                    controller.WaitForStatus(ServiceControllerStatus.Stopped, Remaining(deadline));
                    return;
                default:
                    // Startet gerade (etwa per Fehleraktion nach einem Absturz) und nimmt noch kein
                    // Stop an: abwarten, ob er läuft oder wieder stehen bleibt.
                    if (DateTime.UtcNow >= deadline)
                        throw new System.ServiceProcess.TimeoutException($"Dienst {serviceName} bleibt im Zustand {controller.Status}.");
                    Thread.Sleep(500);
                    break;
            }
        }
    }

    private static TimeSpan Remaining(DateTime deadline)
    {
        var left = deadline - DateTime.UtcNow;
        return left > TimeSpan.FromSeconds(1) ? left : TimeSpan.FromSeconds(1);
    }

    public void Start(TimeSpan timeout)
    {
        using var controller = new ServiceController(serviceName);
        controller.Refresh();
        if (controller.Status == ServiceControllerStatus.Running) return;
        if (controller.Status != ServiceControllerStatus.StartPending) controller.Start();
        controller.WaitForStatus(ServiceControllerStatus.Running, timeout);
    }

    // „Automatisch (verzögert)“ meldet ServiceController als Automatic; zurückgesetzt wird dann auf
    // „Automatisch“. server-install.ps1 richtet den Dienst ohnehin so ein.
    public ServiceStartType GetStartType()
    {
        using var controller = new ServiceController(serviceName);
        return controller.StartType switch
        {
            ServiceStartMode.Automatic or ServiceStartMode.Boot or ServiceStartMode.System => ServiceStartType.Automatic,
            ServiceStartMode.Manual => ServiceStartType.Manual,
            _ => ServiceStartType.Disabled,
        };
    }

    // ServiceController kann den Starttyp nicht setzen; sc.exe gehört zu Windows.
    public void SetStartType(ServiceStartType startType)
    {
        var value = startType switch
        {
            ServiceStartType.Automatic => "auto",
            ServiceStartType.Manual => "demand",
            _ => "disabled",
        };
        var info = new ProcessStartInfo("sc.exe")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
        };
        foreach (var arg in new[] { "config", serviceName, "start=", value }) info.ArgumentList.Add(arg);
        using var process = Process.Start(info) ?? throw new UpdateException("sc.exe ließ sich nicht starten.");
        // Asynchron lesen: Ein blockierendes ReadToEnd vor WaitForExit hebelte das Zeitlimit aus.
        _ = process.StandardOutput.ReadToEndAsync();
        if (!process.WaitForExit(TimeSpan.FromSeconds(30)))
        {
            process.Kill();
            throw new UpdateException("sc.exe config hängt.");
        }
        if (process.ExitCode != 0)
            throw new UpdateException($"Starttyp {value} ließ sich nicht setzen (sc.exe meldet {process.ExitCode}).");
    }

    // server-install.ps1 legt die Umgebung des Dienstes als Multi-String "Environment" in
    // dessen Registry-Schlüssel ab.
    public Uri GetLocalBaseUri()
    {
        using var key = Registry.LocalMachine.OpenSubKey($@"SYSTEM\CurrentControlSet\Services\{serviceName}");
        var environment = key?.GetValue("Environment") as string[];
        const string prefix = "ASPNETCORE_URLS=";
        var urls = environment?.FirstOrDefault(e => e.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))?[prefix.Length..];
        return ServiceUrls.ToLocalBaseUri(urls);
    }
}

internal static class ServiceUrls
{
    public static readonly Uri Fallback = new("http://localhost:5000/");

    // Erste http-Adresse aus ASPNETCORE_URLS; Platzhalter-Hosts (+, *, 0.0.0.0, [::]) → localhost.
    public static Uri ToLocalBaseUri(string? urls)
    {
        if (string.IsNullOrWhiteSpace(urls)) return Fallback;
        foreach (var raw in urls.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!raw.StartsWith("http://", StringComparison.OrdinalIgnoreCase)) continue;
            var normalized = raw
                .Replace("://+", "://localhost", StringComparison.Ordinal)
                .Replace("://*", "://localhost", StringComparison.Ordinal)
                .Replace("://0.0.0.0", "://localhost", StringComparison.Ordinal)
                .Replace("://[::]", "://localhost", StringComparison.Ordinal);
            if (Uri.TryCreate(normalized, UriKind.Absolute, out var uri))
                return new Uri($"http://localhost:{uri.Port}/");
        }
        return Fallback;
    }
}

internal sealed class HttpHealthProbe(HttpClient http) : IHealthProbe
{
    public async Task<string?> GetVersionAsync(Uri baseUri, CancellationToken ct)
    {
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(5));
            var bytes = await http.GetByteArrayAsync(new Uri(baseUri, "api/version"), timeout.Token);
            return JsonSerializer.Deserialize(bytes, UpdaterJson.Default.AppVersionDto)?.Version;
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or JsonException && !ct.IsCancellationRequested)
        {
            return null; // startet noch, Migrationen laufen o. ä.
        }
    }
}

[SupportedOSPlatform("windows")]
internal sealed class IcaclsPermissions : IPermissions
{
    public void ResetToInherited(string directory)
    {
        var info = new ProcessStartInfo("icacls.exe") { UseShellExecute = false, CreateNoWindow = true };
        foreach (var arg in new[] { directory, "/reset", "/T", "/C", "/Q" }) info.ArgumentList.Add(arg);
        using var process = Process.Start(info) ?? throw new UpdateException("icacls ließ sich nicht starten.");
        if (!process.WaitForExit(TimeSpan.FromMinutes(5)))
        {
            process.Kill();
            throw new UpdateException($"icacls /reset für {directory} hängt.");
        }
        if (process.ExitCode != 0) throw new UpdateException($"icacls /reset für {directory} meldet {process.ExitCode}.");
    }

    // Explizite Einträge und Vererbungsschutz übernehmen; geerbte ergeben sich aus dem Elternordner
    // (beide liegen unter Root). Windows vererbt die neue DACL an die Dateien darin.
    public void CopyAccess(string source, string target)
    {
        var from = new DirectoryInfo(source).GetAccessControl(AccessControlSections.Access);
        var to = new DirectorySecurity();
        to.SetAccessRuleProtection(from.AreAccessRulesProtected, preserveInheritance: false);
        foreach (FileSystemAccessRule rule in from.GetAccessRules(includeExplicit: true, includeInherited: false, typeof(SecurityIdentifier)))
            to.AddAccessRule(rule);
        new DirectoryInfo(target).SetAccessControl(to);
    }
}

internal sealed class ProcessApplyLauncher(string serviceName) : IApplyLauncher
{
    public void StartApply(string updaterExe, string root, string packageDirectory, int parentProcessId)
    {
        var info = new ProcessStartInfo(updaterExe)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            // Nicht im staging-Ordner: Ein Arbeitsverzeichnis darin hielte ihn offen.
            WorkingDirectory = root,
        };
        foreach (var arg in new[] { "apply", "--root", root, "--package", packageDirectory, "--wait-pid",
                     parentProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture), "--service", serviceName })
            info.ArgumentList.Add(arg);
        try
        {
            using var _ = Process.Start(info) ?? throw new UpdateException("apply ließ sich nicht starten.");
        }
        catch (Win32Exception ex)
        {
            throw new UpdateException($"apply ließ sich nicht starten: {ex.Message}");
        }
    }
}

internal sealed class AssemblyInstalledVersion : IInstalledVersion
{
    public Version? Read(UpdaterLayout layout)
    {
        if (!File.Exists(layout.InstalledApiDll)) return null;
        var product = FileVersionInfo.GetVersionInfo(layout.InstalledApiDll).ProductVersion;
        return UpdatePackage.TryParseInformational(product, out var version) ? version : null;
    }
}
