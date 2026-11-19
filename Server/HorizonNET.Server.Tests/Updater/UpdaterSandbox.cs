using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using HorizonNET.Shared.Transfer.Update;
using HorizonNET.Updater;

namespace HorizonNET.Server.Tests.Updater;

// Eine Installation in einem Temp-Ordner mit Attrappen für Dienst, Health-Check, Rechte und den
// Start von apply. Die „Version“ eines Programmordners steht in app/version.txt; der Dienst
// „läuft“ mit dem, was dort beim Start steht, und schreibt dabei in die DB (wie eine Migration).
internal sealed class UpdaterSandbox : IDisposable
{
    public UpdaterSandbox()
    {
        Root = Path.Combine(Path.GetTempPath(), "hn-updater-" + Guid.NewGuid().ToString("N"));
        Layout = new UpdaterLayout(Root);
        Layout.EnsureWorkDirectories();
        Directory.CreateDirectory(Layout.Backups); // legt sonst server-install.ps1 an
        Directory.CreateDirectory(Layout.Data);
        Directory.CreateDirectory(Layout.Keys);
        Log = new UpdateLog(Layout.Logs, "test", TimeProvider.System);
        State = new StateFiles(Layout);
        Service = new FakeService(this);
    }

    public string Root { get; }
    public UpdaterLayout Layout { get; }
    public UpdateLog Log { get; }
    public StateFiles State { get; }
    public FakeService Service { get; }
    public FakePermissions Permissions { get; } = new();
    public FakeLauncher Launcher { get; } = new();

    public static UpdaterTimings Timings { get; } = new(
        ServiceTimeout: TimeSpan.FromSeconds(1),
        HealthTimeout: TimeSpan.FromMilliseconds(300),
        PollInterval: TimeSpan.FromMilliseconds(20),
        LockWait: TimeSpan.FromMilliseconds(500),
        IoRetryDelay: TimeSpan.FromMilliseconds(10),
        IoRetries: 3);

    public string DbFile => Path.Combine(Layout.Data, "horizonnet.db");

    // Installierter Stand: app, tools, updater mit Version, dazu DB und Schlüsselring.
    public void Install(string version, bool withUpdater = true)
    {
        WriteProgram(Root, version, withUpdater);
        File.WriteAllText(DbFile, $"db {version}");
        File.WriteAllText(Path.Combine(Layout.Keys, "key-1.xml"), "<key/>");
    }

    public string InstalledVersion(string component = UpdaterLayout.AppComponent) =>
        File.ReadAllText(Path.Combine(Layout.Component(component), "version.txt"));

    // Entpacktes Paket (wie update\staging\X.Y.Z).
    public string CreatePackageDirectory(string version, string? manifestVersion = null)
    {
        var dir = Path.Combine(Root, "pkg-src-" + Guid.NewGuid().ToString("N"));
        WriteProgram(dir, version, withUpdater: true);
        File.WriteAllText(Path.Combine(dir, "manifest.json"),
            $$"""{ "product": "HorizonNET", "version": "{{manifestVersion ?? version}}+abc", "runtimeId": "win-x64", "runtime": "10.0.15" }""");
        return dir;
    }

    // Paket als ZIP mit .sha256 in einem Quellordner (für LocalReleaseSource).
    public string CreatePackageZip(string version, bool corruptChecksum = false)
    {
        var source = Path.Combine(Root, "release-source");
        Directory.CreateDirectory(source);
        var name = $"HorizonNET-{version}-win-x64.zip";
        var zip = Path.Combine(source, name);
        ZipFile.CreateFromDirectory(CreatePackageDirectory(version), zip);
        var hash = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(zip)));
        if (corruptChecksum) hash = new string('0', 64);
        File.WriteAllText(zip + ".sha256", $"{hash}  {name}\n", new UTF8Encoding(false));
        return source;
    }

    public Installer CreateInstaller() =>
        new(Layout, State, Service, Service, Permissions, new VersionFileReader(), Timings, TimeProvider.System, Log);

    public Preparer CreatePreparer(IReleaseSource source) =>
        new(Layout, State, source, new VersionFileReader(), Launcher, CreateInstaller(), Timings, TimeProvider.System, Log);

    private static void WriteProgram(string root, string version, bool withUpdater)
    {
        var parts = new List<(string Component, string Exe)>
        {
            (UpdaterLayout.AppComponent, UpdaterLayout.ApiExe),
            (UpdaterLayout.ToolsComponent, UpdaterLayout.ToolExe),
        };
        if (withUpdater) parts.Add((UpdaterLayout.UpdaterComponent, UpdaterLayout.UpdaterExe));
        foreach (var (component, exe) in parts)
        {
            var dir = Path.Combine(root, component);
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, exe), "exe");
            File.WriteAllText(Path.Combine(dir, "version.txt"), version);
        }
    }

    public void Dispose()
    {
        Log.Dispose();
        try { Directory.Delete(Root, recursive: true); }
        catch (IOException) { /* Temp-Ordner, egal */ }
    }

    internal sealed class FakeService(UpdaterSandbox sandbox) : IServiceHost, IHealthProbe
    {
        public bool Running { get; set; } = true;
        public bool FailStop { get; set; }
        public ServiceStartType StartType { get; set; } = ServiceStartType.Automatic;

        // Diese Version „startet nicht“ (meldet sich nie unter /api/version).
        public string? BrokenVersion { get; set; }
        public List<string> Calls { get; } = [];

        // Wie die Fehleraktion „neu starten“: Solange der Dienst nicht deaktiviert ist, startet Windows
        // ihn nach dem Absturz wieder, und Stop scheitert (oder er läuft danach erneut).
        public bool RestartsWhenEnabled { get; set; }

        public void Stop(TimeSpan timeout)
        {
            Calls.Add("stop");
            if (FailStop) throw new InvalidOperationException("Dienst hängt");
            if (RestartsWhenEnabled && StartType != ServiceStartType.Disabled)
                throw new InvalidOperationException("Dienst startet per Fehleraktion neu");
            Running = false;
        }

        public void Start(TimeSpan timeout)
        {
            Calls.Add("start");
            // Wie der Dienststeuerungs-Manager: Ein deaktivierter Dienst startet nicht.
            if (StartType == ServiceStartType.Disabled) throw new InvalidOperationException("Dienst ist deaktiviert");
            Running = true;
            var version = sandbox.InstalledVersion();
            // Wie eine Migration: Die neue Version verändert die DB beim Start.
            File.WriteAllText(sandbox.DbFile, $"db migrated by {version}");
        }

        public ServiceStartType GetStartType() => StartType;

        // Das n-te Deaktivieren scheitert (wie sc.exe mit Zeitlimit oder Exit-Code ≠ 0).
        public int FailDisableAt { get; set; } = int.MaxValue;
        private int _disables;

        public void SetStartType(ServiceStartType startType)
        {
            Calls.Add("starttype " + startType);
            if (startType == ServiceStartType.Disabled && ++_disables >= FailDisableAt)
                throw new InvalidOperationException("sc.exe meldet 1053");
            StartType = startType;
        }

        public Uri GetLocalBaseUri() => new("http://localhost:5000/");

        public Task<string?> GetVersionAsync(Uri baseUri, CancellationToken ct)
        {
            if (!Running) return Task.FromResult<string?>(null);
            var version = sandbox.InstalledVersion();
            return Task.FromResult<string?>(version == BrokenVersion ? null : version + "+commit");
        }
    }

    internal sealed class FakePermissions : IPermissions
    {
        public List<string> Reset { get; } = [];
        public List<(string Source, string Target)> Copied { get; } = [];
        public void ResetToInherited(string directory) => Reset.Add(Path.GetFileName(directory));

        public void CopyAccess(string source, string target) =>
            Copied.Add((Path.GetFileName(source), Path.GetFileName(target)));
    }

    internal sealed class FakeLauncher : IApplyLauncher
    {
        public (string Exe, string Root, string Package)? Started { get; private set; }

        public void StartApply(string updaterExe, string root, string packageDirectory, int parentProcessId) =>
            Started = (updaterExe, root, packageDirectory);
    }

    private sealed class VersionFileReader : IInstalledVersion
    {
        public Version? Read(UpdaterLayout layout)
        {
            var file = Path.Combine(layout.Component(UpdaterLayout.AppComponent), "version.txt");
            return File.Exists(file) && UpdatePackage.TryParseInformational(File.ReadAllText(file), out var v) ? v : null;
        }
    }
}
