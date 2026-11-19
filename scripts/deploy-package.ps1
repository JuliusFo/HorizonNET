<#
.SYNOPSIS
    Schnürt das Deployment-Paket für den vServer.

.DESCRIPTION
    Erstellt unter publish\ ein ZIP mit allem, was der Server braucht:
      - app\           Release-Publish der API (liefert auch den Blazor-Client aus)
      - tools\         HorizonNET.KeyMigration (für den Schlüsselring-Umzug, siehe
                       docs\konzept-dpapi-umzug.md)
      - updater\       HorizonNET.Updater als eine Datei (Auto-Update, ADR 0007)
      - server-install.ps1 (Einrichtung auf dem Server, einmalig)
      - manifest.json  Version, Commit, Build-Zeit, Runtime-ID und mitgelieferte Runtime

    API, Tool und Updater sind self-contained (win-x64) veröffentlicht: Das Paket bringt die
    .NET- und ASP.NET-Core-Runtime mit, auf dem Server muss keine installiert sein
    (ADR 0005, Nachtrag 2026-11-19). Das Dockerfile veröffentlicht weiterhin
    framework-abhängig; die Einstellung steckt bewusst nur hier, nicht in den .csproj.

    Neben das ZIP legt das Skript HorizonNET-<Version>-win-x64.zip.sha256 (Format wie
    sha256sum). Die Prüfsumme schützt vor einem kaputten Download, nicht vor einem
    manipulierten Paket.

    Die mitgelieferte Runtime ist der Patch der Microsoft-Pakete (Microsoft.AspNetCore.*,
    Microsoft.Extensions.*) aus Directory.Packages.props, nicht die des installierten SDK.
    Ein Runtime-Sicherheitsupdate heißt also: Pakete heben, Release, Update einspielen.

    Das Skript bricht ab, wenn
      - appsettings.Secrets.json oder appsettings.Development.json im Paket landen würde,
      - die Runtime im Paket nicht genau diesem Patch entspricht.

    Das ZIP auf den vServer kopieren, entpacken, dort server-install.ps1 als
    Administrator ausführen. publish\ ist git-ignoriert.

.PARAMETER OutputDirectory
    Zielordner für ZIP und Prüfsumme, absolut oder relativ zum aktuellen Ordner.
    Standard: <repo>\publish. Der Release-Workflow (.github/workflows/release.yml) baut
    das Paket mit diesem Skript und hängt es an das GitHub-Release.

.EXAMPLE
    .\scripts\deploy-package.ps1
#>
param(
    [string]$OutputDirectory
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
# Absolut auflösen: .NET-Aufrufe ([IO.File], ZipFile) rechnen relative Pfade gegen ihr eigenes
# Arbeitsverzeichnis, nicht gegen das von PowerShell (release.yml übergibt "package").
$publishRoot = if ($OutputDirectory) {
    $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($OutputDirectory)
} else { Join-Path $repoRoot 'publish' }
$staging = Join-Path $publishRoot 'staging'
$runtimeId = 'win-x64'

if (Test-Path $staging) { Remove-Item $staging -Recurse -Force }
New-Item -ItemType Directory -Force $staging | Out-Null

# Runtime-Version = Patch der Microsoft-Pakete (ASP.NET Core und Extensions erscheinen im
# Gleichschritt mit der Runtime). Ohne Vorgabe nähme der Publish die Runtime, die das gerade
# installierte SDK kennt – die kann hinter den Paketen liegen (SDK 10.0.401: Runtime 10.0.12
# bei Paketen auf 10.0.15). So bestimmt allein Directory.Packages.props, was ins Paket kommt.
[xml]$packagesProps = Get-Content (Join-Path $repoRoot 'Directory.Packages.props') -Raw
$runtimeVersion = $packagesProps.Project.ItemGroup.PackageVersion |
    Where-Object { $_.Include -match '^Microsoft\.(AspNetCore|Extensions)\.' } |
    ForEach-Object { [version]$_.Version } |
    Sort-Object -Descending |
    Select-Object -First 1
if (-not $runtimeVersion) { throw 'Keine Microsoft.AspNetCore.*-/Microsoft.Extensions.*-Pakete in Directory.Packages.props gefunden.' }

# Self-contained, aber kein Single-File: Die API liefert den Client als statische Dateien
# aus, und /api/version liest die Build-Zeit aus dem Pfad der Assembly.
# HorizonNET.Api.csproj und HorizonNET.KeyMigration.csproj setzen daraus RuntimeFrameworkVersion;
# die gilt für alle Framework-Referenzen, also auch Microsoft.AspNetCore.App.
$publishArgs = @('-c', 'Release', '-r', $runtimeId, '--self-contained', '--nologo',
    "-p:HorizonRuntimeVersion=$runtimeVersion")

Write-Host 'Publish der API (Release, self-contained)…'
dotnet publish (Join-Path $repoRoot 'Server\HorizonNET.Api') @publishArgs -o (Join-Path $staging 'app')
if ($LASTEXITCODE -ne 0) { throw 'Publish der API fehlgeschlagen.' }

Write-Host 'Publish des Umzugs-Tools (self-contained)…'
dotnet publish (Join-Path $repoRoot 'Tools\HorizonNET.KeyMigration') @publishArgs -o (Join-Path $staging 'tools')
if ($LASTEXITCODE -ne 0) { throw 'Publish des Tools fehlgeschlagen.' }

Write-Host 'Publish des Updaters (self-contained, eine Datei)…'
dotnet publish (Join-Path $repoRoot 'Tools\HorizonNET.Updater') @publishArgs -o (Join-Path $staging 'updater')
if ($LASTEXITCODE -ne 0) { throw 'Publish des Updaters fehlgeschlagen.' }
# Die .pdb braucht auf dem Server niemand; das Log nennt Fehler ohne Zeilennummern.
Get-ChildItem (Join-Path $staging 'updater') -Filter '*.pdb' | Remove-Item

Copy-Item (Join-Path $PSScriptRoot 'server-install.ps1') $staging

# ── Prüfung: keine Entwicklungs-Secrets im Paket ─────────────────────────────
$forbidden = Get-ChildItem $staging -Recurse -File |
    Where-Object { $_.Name -in 'appsettings.Secrets.json', 'appsettings.Development.json' }
if ($forbidden) {
    throw "Entwicklungs-Konfiguration im Paket: $(($forbidden | ForEach-Object FullName) -join ', ')"
}

# ── Prüfung: mitgelieferte Runtime passt zu den Microsoft-Paketen ───────────
function Get-IncludedFrameworks([string]$runtimeConfig) {
    $config = Get-Content $runtimeConfig -Raw | ConvertFrom-Json
    $frameworks = @{}
    foreach ($fw in $config.runtimeOptions.includedFrameworks) {
        $frameworks[$fw.name] = [version]$fw.version
    }
    if (-not $frameworks.ContainsKey('Microsoft.NETCore.App')) {
        throw "$runtimeConfig nennt keine mitgelieferte Runtime – ist der Publish wirklich self-contained?"
    }
    return $frameworks
}

$apiFrameworks = Get-IncludedFrameworks (Join-Path $staging 'app\HorizonNET.Api.runtimeconfig.json')
$toolFrameworks = Get-IncludedFrameworks (Join-Path $staging 'tools\HorizonNET.KeyMigration.runtimeconfig.json')
$aspNetRuntime = $apiFrameworks['Microsoft.AspNetCore.App']
if (-not $aspNetRuntime) { throw 'Die API bringt keine ASP.NET Core Runtime mit.' }

# Gegenprobe: Hat der Publish die vorgegebene Runtime wirklich übernommen?
$included = @{
    'API: Microsoft.NETCore.App'     = $apiFrameworks['Microsoft.NETCore.App']
    'API: Microsoft.AspNetCore.App'  = $aspNetRuntime
    'Tool: Microsoft.NETCore.App'    = $toolFrameworks['Microsoft.NETCore.App']
}
foreach ($entry in $included.GetEnumerator()) {
    if ($entry.Value -ne $runtimeVersion) {
        throw "Runtime im Paket passt nicht zu den Microsoft-Paketen: $($entry.Key) ist $($entry.Value), erwartet $runtimeVersion."
    }
}
Write-Host "Runtime im Paket: $runtimeVersion (.NET und ASP.NET Core, wie die Microsoft-Pakete)"

# ── Version, Manifest, ZIP, Prüfsumme ───────────────────────────────────────
# InformationalVersion ist "X.Y.Z" oder "X.Y.Z+<commit>" (SourceLink hängt den Commit an).
$dll = Join-Path $staging 'app\HorizonNET.Api.dll'
$informational = [Diagnostics.FileVersionInfo]::GetVersionInfo($dll).ProductVersion
$version, $commit = $informational -split '\+', 2
if (-not $commit) {
    # Unter PowerShell 5.1 mit 'Stop' wirft schon eine Zeile auf stderr – deshalb try.
    try { $commit = (git -C $repoRoot rev-parse HEAD 2>$null) } catch { $commit = $null }
    if ($LASTEXITCODE -ne 0) { $commit = $null }
}

$manifest = [ordered]@{
    product   = 'HorizonNET'
    version   = $version
    commit    = $commit
    builtUtc  = [DateTime]::UtcNow.ToString('yyyy-MM-ddTHH:mm:ssZ')
    runtimeId = $runtimeId
    runtime   = $runtimeVersion.ToString()
}
# UTF-8 ohne BOM, damit jeder JSON-Leser die Datei nimmt (Set-Content -Encoding UTF8
# schreibt unter Windows PowerShell 5.1 ein BOM).
[IO.File]::WriteAllText((Join-Path $staging 'manifest.json'),
    ($manifest | ConvertTo-Json -Depth 3), [Text.UTF8Encoding]::new($false))

$zipName = "HorizonNET-$version-$runtimeId.zip"
$zip = Join-Path $publishRoot $zipName
$shaFile = "$zip.sha256"
foreach ($old in $zip, $shaFile) { if (Test-Path $old) { Remove-Item $old -Force } }

# Einträge einzeln anlegen: Unter Windows PowerShell 5.1 (.NET Framework) schreiben
# Compress-Archive und ZipFile.CreateFromDirectory "\" als Trenner in die Einträge – der
# ZIP-Standard verlangt "/", und nicht jeder Entpacker kommt mit "\" zurecht.
Add-Type -AssemblyName System.IO.Compression, System.IO.Compression.FileSystem
$stagingFull = (Resolve-Path $staging).Path.TrimEnd('\') + '\'
$archive = [IO.Compression.ZipFile]::Open($zip, [IO.Compression.ZipArchiveMode]::Create)
try {
    foreach ($file in Get-ChildItem $staging -Recurse -File) {
        $entryName = $file.FullName.Substring($stagingFull.Length).Replace('\', '/')
        [void][IO.Compression.ZipFileExtensions]::CreateEntryFromFile(
            $archive, $file.FullName, $entryName, [IO.Compression.CompressionLevel]::Optimal)
    }
}
finally {
    $archive.Dispose()
}
Remove-Item $staging -Recurse -Force

$hash = (Get-FileHash $zip -Algorithm SHA256).Hash.ToLowerInvariant()
[IO.File]::WriteAllText($shaFile, "$hash  $zipName`n", [Text.UTF8Encoding]::new($false))

$sizeMb = [math]::Round((Get-Item $zip).Length / 1MB, 1)
Write-Host "Fertig: $zip ($sizeMb MB)"
Write-Host "SHA-256: $hash"
Write-Host 'Nächster Schritt: ZIP auf den vServer kopieren, entpacken, server-install.ps1 als Administrator ausführen.'
