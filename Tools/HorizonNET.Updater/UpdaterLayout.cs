namespace HorizonNET.Updater;

// Ordner einer Installation (Standard C:\HorizonNET), siehe docs/konzept-auto-update.md, Abschnitt 6.
internal sealed class UpdaterLayout
{
    public const string AppComponent = "app";
    public const string ToolsComponent = "tools";
    public const string UpdaterComponent = "updater";

    // Reihenfolge des Tauschs; der Updater zuletzt (beim Zurückrollen also zuerst).
    public static readonly string[] Components = [AppComponent, ToolsComponent, UpdaterComponent];

    public const string ApiExe = "HorizonNET.Api.exe";
    public const string ApiDll = "HorizonNET.Api.dll";
    public const string ToolExe = "HorizonNET.KeyMigration.exe";
    public const string UpdaterExe = "HorizonNET.Updater.exe";

    public UpdaterLayout(string root)
    {
        Root = Path.GetFullPath(root);
    }

    public string Root { get; }

    public string Component(string name) => Path.Combine(Root, name);
    public string Previous(string name) => Path.Combine(Root, name + ".previous");

    public string Data => Path.Combine(Root, "data");
    public string Keys => Path.Combine(Root, "keys");
    public string Backups => Path.Combine(Root, "backups");

    public string UpdateDir => Path.Combine(Root, "update");
    public string Downloads => Path.Combine(UpdateDir, "downloads");
    public string Staging => Path.Combine(UpdateDir, "staging");
    public string Logs => Path.Combine(UpdateDir, "logs");
    public string RequestDir => Path.Combine(UpdateDir, "request");
    public string RequestFile => Path.Combine(RequestDir, "request.json");
    public string StatusFile => Path.Combine(UpdateDir, "status.json");
    public string JournalFile => Path.Combine(UpdateDir, "journal.json");
    public string LockFile => Path.Combine(UpdateDir, "update.lock");

    public string InstalledApiDll => Path.Combine(Component(AppComponent), ApiDll);

    public string StagingFor(Version version) => Path.Combine(Staging, Shared.Transfer.Update.UpdatePackage.Format(version));

    public void EnsureWorkDirectories()
    {
        foreach (var dir in new[] { UpdateDir, Downloads, Staging, Logs, RequestDir, Backups })
            Directory.CreateDirectory(dir);
    }
}
