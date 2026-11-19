using System.Diagnostics;
using System.Globalization;
using HorizonNET.Shared.Transfer.Update;
using HorizonNET.Updater;

// Updater für den vServer (ADR 0007, docs/konzept-auto-update.md).
//
//   run      [--root <dir>]                      geplante Aufgabe „HorizonNET Recover“ (Systemstart):
//                                                nur ein unterbrochenes Update zurückrollen
//   install  [--root <dir>] [--source <ordner>]  geplante Aufgabe „HorizonNET Update“ (startet die App)
//                                                oder von Hand: zurückrollen falls nötig, dann neuestes
//                                                Release installieren; --source nur von Hand
//   status   [--root <dir>]                      status.json ausgeben
//   apply    --root <dir> --package <dir> [--wait-pid <pid>]   intern, startet run/install
//
// Gemeinsam: --service <name> (Standard HorizonNET). --root fehlt → der Ordner über updater\.
//
// Exit-Codes: 0 ok oder nichts zu tun, 1 gescheitert (nichts geändert bzw. zurückgerollt),
//             2 Zurückrollen gescheitert, 3 falscher Aufruf.

Console.OutputEncoding = System.Text.Encoding.UTF8;
var command = args.FirstOrDefault();
if (command is null or "help" or "--help" or "-h") return Usage();

if (!OperatingSystem.IsWindows())
{
    Console.Error.WriteLine("Der Updater läuft nur unter Windows.");
    return 3;
}

var root = Option("--root") ?? DefaultRoot();
if (root is null)
{
    Console.Error.WriteLine("--root fehlt (der Updater liegt nicht in <root>\\updater\\).");
    return 3;
}

var layout = new UpdaterLayout(root);
if (command == "status")
{
    Console.WriteLine(File.Exists(layout.StatusFile) ? File.ReadAllText(layout.StatusFile) : "Noch kein Update gelaufen.");
    return 0;
}

if (command is not ("run" or "install" or "apply")) return Usage();

layout.EnsureWorkDirectories();
var time = TimeProvider.System;
var timings = UpdaterTimings.Default;
using var log = new UpdateLog(layout.Logs, command, time);
using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cts.Cancel();
};

// Kurzes Zeitlimit für die GitHub-API und /api/version, langes nur für den Download des Pakets.
using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
http.DefaultRequestHeaders.UserAgent.ParseAdd(UpdatePackage.UserAgent);
using var downloadHttp = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
downloadHttp.DefaultRequestHeaders.UserAgent.ParseAdd(UpdatePackage.UserAgent);

var state = new StateFiles(layout);
var serviceName = Option("--service") ?? "HorizonNET";
var service = new WindowsServiceHost(serviceName);
var installed = new AssemblyInstalledVersion();
var installer = new Installer(layout, state, service, new HttpHealthProbe(http), new IcaclsPermissions(),
    installed, timings, time, log);

try
{
    log.Info($"HorizonNET.Updater {typeof(Installer).Assembly.GetName().Version} – {command}, Root {layout.Root}");
    switch (command)
    {
        case "apply":
        {
            var package = Option("--package");
            if (package is null) return Usage();
            if (int.TryParse(Option("--wait-pid"), NumberStyles.None, CultureInfo.InvariantCulture, out var pid))
                await WaitForExitAsync(pid, timings.LockWait);
            return await installer.ApplyAsync(Path.GetFullPath(package), cts.Token);
        }
        default:
        {
            var sourceDir = command == "install" ? Option("--source") : null;
            IReleaseSource source = sourceDir is null ? new GitHubReleaseSource(http, downloadHttp) : new LocalReleaseSource(Path.GetFullPath(sourceDir));
            var preparer = new Preparer(layout, state, source, installed, new ProcessApplyLauncher(serviceName), installer, timings, time, log);
            return await preparer.RunAsync(install: command == "install", cts.Token);
        }
    }
}
catch (Exception ex)
{
    log.Error($"Unerwarteter Fehler: {ex}");
    return 1;
}

string? Option(string name)
{
    var index = Array.IndexOf(args, name);
    return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
}

static string? DefaultRoot()
{
    var dir = new DirectoryInfo(AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar));
    return dir.Name.Equals(UpdaterLayout.UpdaterComponent, StringComparison.OrdinalIgnoreCase) ? dir.Parent?.FullName : null;
}

static async Task WaitForExitAsync(int pid, TimeSpan timeout)
{
    try
    {
        using var parent = Process.GetProcessById(pid);
        using var cts = new CancellationTokenSource(timeout);
        await parent.WaitForExitAsync(cts.Token);
    }
    catch (ArgumentException)
    {
        // schon beendet
    }
    catch (OperationCanceledException)
    {
        // apply läuft trotzdem; die Sperre und die Wiederholungen beim Verschieben fangen den Rest.
    }
}

static int Usage()
{
    Console.Error.WriteLine("""
        HorizonNET.Updater – Auto-Update für den vServer (ADR 0007)

          run      [--root <dir>]                     unterbrochenes Update zurückrollen (Systemstart)
          install  [--root <dir>] [--source <ordner>] neuestes Release installieren
          status   [--root <dir>]                     letzten Status zeigen

          --service <name>  Name des Windows-Dienstes (Standard: HorizonNET)
        """);
    return 3;
}
