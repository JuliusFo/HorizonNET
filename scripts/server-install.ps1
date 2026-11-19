<#
.SYNOPSIS
    Richtet HorizonNET auf dem vServer ein oder aktualisiert die Einrichtung (als Administrator).

.DESCRIPTION
    Führt auf dem Server die Einrichtung aus docs\deployment-vserver.md aus:
      1. Ordnerstruktur unter -Root (app, tools, updater, data, keys, update, backups)
      2. Dediziertes lokales Dienstkonto "horizonnet" (DPAPI bindet den Schlüsselring
         an genau dieses Konto – deshalb kein LocalSystem)
      3. Ordnerrechte (ADR 0007): Das Dienstkonto liest und führt aus, schreiben darf es nur
         in data\, keys\ und update\request\. Programmordner kann es also nicht tauschen –
         wichtig, weil der Updater sie als SYSTEM startet.
      4. App, Tools und Updater aus dem entpackten Deployment-Paket kopieren (Inhalt ersetzen)
      5. Windows-Dienst inkl. Umgebungsvariablen (Secrets werden abgefragt, nie geloggt)
      6. Geplante Aufgabe "HorizonNET Update" (SYSTEM, beim Systemstart und auf Anforderung
         der App); das Dienstkonto darf genau diese Aufgabe starten, sonst nichts.

    Bei der Ersteinrichtung bleibt der Dienst GESTOPPT: Erst müssen DB + Schlüsselring per
    Umzugs-Tool ankommen (docs\konzept-dpapi-umzug.md). Lief der Dienst schon, wird er für das
    Kopieren gestoppt und danach wieder gestartet.

    Erneut ausführen ist ungefährlich: Bestehendes wird aktualisiert statt doppelt
    angelegt. Mit -SkipService bleiben Konto, Dienst und dessen Secrets unverändert (keine
    Passwortabfrage); nur die Update-Variablen des Dienstes werden gesetzt.

.PARAMETER Domain
    Öffentlicher Hostname hinter dem Tunnel, z. B. horizon.example.de (für AllowedHosts).
    Pflicht, außer mit -SkipService.

.PARAMETER Root
    Zielordner der Installation. Standard: C:\HorizonNET

.PARAMETER Port
    Lokaler HTTP-Port, auf den cloudflared zeigt. Standard: 5000

.PARAMETER UpdateMode
    Install (Standard): Die App meldet neue Versionen und installiert auf Klick.
    Notify: nur melden. Off: nicht prüfen.

.PARAMETER SkipService
    Konto, Anmelderechte und Dienstkonfiguration nicht anfassen. Für das erneute Ausführen
    auf einem eingerichteten Server, etwa beim Umstieg auf das Auto-Update.

.EXAMPLE
    .\server-install.ps1 -Domain horizon.example.de

.EXAMPLE
    .\server-install.ps1 -SkipService
#>
param(
    [string]$Domain,
    [string]$Root = 'C:\HorizonNET',
    [int]$Port = 5000,
    [string]$ServiceName = 'HorizonNET',
    [string]$ServiceUser = 'horizonnet',
    [string]$UpdateTaskName = 'HorizonNET Update',
    [ValidateSet('Install', 'Notify', 'Off')][string]$UpdateMode = 'Install',
    [switch]$SkipService
)

$ErrorActionPreference = 'Stop'

if (-not ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()
        ).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw 'Bitte als Administrator ausführen.'
}

$package = $PSScriptRoot
foreach ($required in 'app\HorizonNET.Api.exe', 'tools\HorizonNET.KeyMigration.exe', 'updater\HorizonNET.Updater.exe') {
    if (-not (Test-Path (Join-Path $package $required))) {
        throw "Kein vollständiges Deployment-Paket – dieses Skript aus dem entpackten ZIP heraus starten ($required fehlt neben $package)."
    }
}

if ($SkipService) {
    if (-not (Get-Service $ServiceName -ErrorAction SilentlyContinue)) {
        throw "-SkipService: Dienst '$ServiceName' existiert nicht. Erst ohne -SkipService einrichten."
    }
    if (-not (Get-LocalUser -Name $ServiceUser -ErrorAction SilentlyContinue)) {
        throw "-SkipService: Dienstkonto '$ServiceUser' existiert nicht."
    }
}
elseif (-not $Domain) {
    throw '-Domain fehlt (nur mit -SkipService entbehrlich).'
}

# Gut bekannte SIDs statt Namen: "Administratoren" heißt nur auf deutschem Windows so.
$system = '*S-1-5-18'
$admins = '*S-1-5-32-544'

function Invoke-Icacls {
    param([string[]]$Arguments)
    & icacls.exe @Arguments | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "icacls $($Arguments -join ' ') ist fehlgeschlagen ($LASTEXITCODE)." }
}

# ── 1) Ordner ────────────────────────────────────────────────────────────────
foreach ($dir in 'app', 'tools', 'updater', 'data', 'keys', 'backups',
                 'update', 'update\downloads', 'update\staging', 'update\logs', 'update\request') {
    New-Item -ItemType Directory -Force (Join-Path $Root $dir) | Out-Null
}

# ── 2) Dienstkonto ───────────────────────────────────────────────────────────
$password = $null
if (-not $SkipService) {
    $existing = Get-LocalUser -Name $ServiceUser -ErrorAction SilentlyContinue
    if ($existing) {
        Write-Host "Dienstkonto '$ServiceUser' existiert bereits – Passwort wird für die Dienstanmeldung erneut benötigt."
        Write-Host '(Nur Dateien und Update-Einrichtung erneuern? Dann mit -SkipService aufrufen.)'
        $password = Read-Host "Passwort des bestehenden Kontos '$ServiceUser'" -AsSecureString
    }
    else {
        # Zufallspasswort: Man braucht es später für "runas" beim DPAPI-Umzug – wird einmal angezeigt.
        $rng = [Security.Cryptography.RNGCryptoServiceProvider]::new()
        $bytes = New-Object byte[] 24; $rng.GetBytes($bytes)
        $plain = [Convert]::ToBase64String($bytes) + '!a1'
        $password = ConvertTo-SecureString $plain -AsPlainText -Force
        # Beschreibung: New-LocalUser erlaubt höchstens 48 Zeichen.
        New-LocalUser -Name $ServiceUser -Password $password -PasswordNeverExpires -AccountNeverExpires `
            -Description 'HorizonNET-Dienstkonto (DPAPI-Bindung)' | Out-Null
        Write-Host ''
        Write-Host "Dienstkonto '$ServiceUser' angelegt. PASSWORT NOTIEREN (wird für den DPAPI-Umzug per runas gebraucht):"
        Write-Host "  $plain"
        Write-Host ''
    }
}
$sid = (Get-LocalUser -Name $ServiceUser).SID.Value

if (-not $SkipService) {
    # Anmelderechte per secedit ergänzen:
    #   SeServiceLogonRight – der Dienst selbst
    #   SeBatchLogonRight   – geplante Aufgaben (DPAPI-Umzug läuft als Task unter diesem
    #                         Konto, weil runas/Get-Credential in SSH-Sitzungen scheitern)
    $inf = Join-Path $env:TEMP 'hn-rights.inf'
    $db  = Join-Path $env:TEMP 'hn-rights.sdb'
    secedit /export /cfg $inf /areas USER_RIGHTS | Out-Null
    $content = Get-Content $inf
    $changed = $false
    foreach ($right in 'SeServiceLogonRight', 'SeBatchLogonRight') {
        $line = $content | Where-Object { $_ -match "^$right" }
        if ($line -match [regex]::Escape($sid)) { continue }
        $newLine = if ($line) { "$line,*$sid" } else { "$right = *$sid" }
        $content = if ($line) { $content -replace [regex]::Escape($line), $newLine }
                   else { $content + $newLine }
        $changed = $true
        Write-Host "'$right' für $ServiceUser gesetzt."
    }
    if ($changed) {
        Set-Content $inf $content -Encoding Unicode
        secedit /configure /db $db /cfg $inf /areas USER_RIGHTS | Out-Null
    }
    Remove-Item $inf, $db -ErrorAction SilentlyContinue
}

# ── 3) Dienst für das Kopieren anhalten ─────────────────────────────────────
$service = Get-Service $ServiceName -ErrorAction SilentlyContinue
$wasRunning = $service -and $service.Status -eq 'Running'
if ($wasRunning) {
    Write-Host "Stoppe Dienst '$ServiceName'…"
    Stop-Service $ServiceName
    (Get-Service $ServiceName).WaitForStatus('Stopped', [TimeSpan]::FromSeconds(60))
}

# ── 4) Rechte ────────────────────────────────────────────────────────────────
# Frühere Einrichtungen gaben dem Dienstkonto "Ändern" auf alles (auch als explizite Einträge
# an jeder Datei). Die fallen zuerst weg, dann gilt die Tabelle aus docs\konzept-auto-update.md:
#   Root (und damit app, tools, updater, update): SYSTEM/Admins voll, Dienstkonto Lesen/Ausführen
#   data, update\request: Dienstkonto zusätzlich Ändern
#   keys: nur Dienstkonto, SYSTEM (für die Sicherung) und Admins
#   backups: nur SYSTEM und Admins (enthält den Schlüsselring)
Write-Host 'Setze Ordnerrechte…'
Invoke-Icacls @($Root, '/remove:g', $ServiceUser, '/T', '/C', '/Q')
Invoke-Icacls @($Root, '/inheritance:r', '/grant:r', "${system}:(OI)(CI)F", "${admins}:(OI)(CI)F",
    "${ServiceUser}:(OI)(CI)RX", '/Q')
Invoke-Icacls @((Join-Path $Root 'data'), '/grant:r', "${ServiceUser}:(OI)(CI)M", '/Q')
Invoke-Icacls @((Join-Path $Root 'update\request'), '/grant:r', "${ServiceUser}:(OI)(CI)M", '/Q')
Invoke-Icacls @((Join-Path $Root 'keys'), '/inheritance:r', '/grant:r', "${ServiceUser}:(OI)(CI)F",
    "${system}:(OI)(CI)F", "${admins}:(OI)(CI)F", '/Q')
Invoke-Icacls @((Join-Path $Root 'backups'), '/inheritance:r', '/grant:r', "${system}:(OI)(CI)F",
    "${admins}:(OI)(CI)F", '/Q')

# ── 5) Dateien ───────────────────────────────────────────────────────────────
# Inhalt ersetzen statt darüberkopieren: Seit dem self-contained Paket liegen andere Dateien
# im Ordner, Reste einer älteren Version sollen nicht mitladen.
Write-Host 'Kopiere App, Tools und Updater…'
foreach ($component in 'app', 'tools', 'updater') {
    $target = Join-Path $Root $component
    Get-ChildItem $target -Force | Remove-Item -Recurse -Force
    Copy-Item (Join-Path $package "$component\*") $target -Recurse -Force
}

# ── 6) Dienst + Umgebungsvariablen ──────────────────────────────────────────
$updateVars = @(
    "Update__Mode=$UpdateMode"
    "Update__Directory=$Root\update"
    "Update__TaskName=$UpdateTaskName"
)
$registryPath = "HKLM:\SYSTEM\CurrentControlSet\Services\$ServiceName"

if ($SkipService) {
    # Bestehende Variablen (Secrets!) behalten, nur die Update-Variablen ersetzen.
    $current = @((Get-ItemProperty $registryPath -Name 'Environment' -ErrorAction SilentlyContinue).Environment)
    $envVars = @($current | Where-Object { $_ -and $_ -notmatch '^Update__' }) + $updateVars
}
else {
    $googleId     = Read-Host 'Google ClientId (leer = überspringen, später nachtragbar)'
    $googleSecret = if ($googleId) { Read-Host 'Google ClientSecret' } else { '' }

    $envVars = @(
        'ASPNETCORE_ENVIRONMENT=Production'
        "ASPNETCORE_URLS=http://localhost:$Port"
        "AllowedHosts=$Domain;localhost"
        "ConnectionStrings__DefaultConnection=Data Source=$Root\data\horizonnet.db"
        "DataProtection__KeyRingPath=$Root\keys"
    ) + $updateVars
    if ($googleId) {
        $envVars += "Google__ClientId=$googleId"
        $envVars += "Google__ClientSecret=$googleSecret"
    }

    $cred = New-Object Management.Automation.PSCredential(".\$ServiceUser", $password)
    $binPath = Join-Path $Root 'app\HorizonNET.Api.exe'

    if (Get-Service $ServiceName -ErrorAction SilentlyContinue) {
        Write-Host "Dienst '$ServiceName' existiert – Konfiguration wird aktualisiert."
        sc.exe config $ServiceName binPath= "$binPath" obj= ".\$ServiceUser" password= "$($cred.GetNetworkCredential().Password)" | Out-Null
    }
    else {
        New-Service -Name $ServiceName -BinaryPathName $binPath -DisplayName 'HorizonNET' `
            -Description 'HorizonNET API + App (Same-Origin, hinter Cloudflare Tunnel)' `
            -StartupType Automatic -Credential $cred | Out-Null
    }
    # Nach Absturz automatisch neu starten (5 s Pause)
    sc.exe failure $ServiceName reset= 86400 actions= restart/5000/restart/5000/restart/5000 | Out-Null
}

# Env-Vars gelten NUR für diesen Dienst (Registry Multi-String "Environment")
Set-ItemProperty -Path $registryPath -Name 'Environment' -Value $envVars -Type MultiString

# ── 7) Geplante Aufgabe für den Updater ─────────────────────────────────────
# Läuft als SYSTEM. Beim Systemstart rollt sie nur ein unterbrochenes Update zurück; installiert
# wird erst, wenn die App update\request\request.json schreibt und die Aufgabe startet.
Write-Host "Richte geplante Aufgabe '$UpdateTaskName' ein…"
$updaterExe = Join-Path $Root 'updater\HorizonNET.Updater.exe'
$action = New-ScheduledTaskAction -Execute $updaterExe `
    -Argument "run --root `"$Root`" --service `"$ServiceName`"" -WorkingDirectory (Join-Path $Root 'update')
$trigger = New-ScheduledTaskTrigger -AtStartup
$principal = New-ScheduledTaskPrincipal -UserId 'SYSTEM' -LogonType ServiceAccount -RunLevel Highest
$settings = New-ScheduledTaskSettingsSet -ExecutionTimeLimit (New-TimeSpan -Hours 1) -MultipleInstances IgnoreNew `
    -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -StartWhenAvailable
Register-ScheduledTask -TaskName $UpdateTaskName -Action $action -Trigger $trigger -Principal $principal `
    -Settings $settings -Description 'HorizonNET: Update installieren (ADR 0007)' -Force | Out-Null

# Startrecht für das Dienstkonto: Lesen + Ausführen (GRGX) auf genau diese Aufgabe. Mehr kann es
# damit nicht tun – der Updater nimmt aus der Anfrage weder Pfade noch Versionen.
$scheduler = New-Object -ComObject 'Schedule.Service'
$scheduler.Connect()
$task = $scheduler.GetFolder('\').GetTask($UpdateTaskName)
$dacl = $task.GetSecurityDescriptor(4)   # DACL_SECURITY_INFORMATION
if ($dacl -notmatch [regex]::Escape($sid)) {
    $task.SetSecurityDescriptor($dacl + "(A;;GRGX;;;$sid)", 0)
    Write-Host "'$ServiceUser' darf die Aufgabe '$UpdateTaskName' starten."
}

# ── 8) Abschluss ─────────────────────────────────────────────────────────────
Write-Host ''
Write-Host '════════════════════════════════════════════════════════════'
if ($wasRunning) {
    Start-Service $ServiceName
    Write-Host "Aktualisiert. Dienst '$ServiceName' läuft wieder."
    Write-Host "  Test:  Invoke-WebRequest http://localhost:$Port/api/version"
    Write-Host "  Update-Modus: $UpdateMode (Einstellungen → Über & Updates)"
    return
}

if ($SkipService) {
    Write-Host "Aktualisiert. Der Dienst lief vorher nicht und bleibt gestoppt:  Start-Service $ServiceName"
    return
}

Write-Host 'Einrichtung fertig. Der Dienst ist absichtlich noch GESTOPPT.'
Write-Host ''
Write-Host 'Nächste Schritte (Details: docs\konzept-dpapi-umzug.md):'
Write-Host "  1. DB-Kopie + Transport-Ring vom Entwicklungsrechner nach $Root\data bzw. einen Temp-Ordner bringen"
Write-Host "  2. ALS '$ServiceUser' (runas) umschlüsseln:"
Write-Host "       runas /user:.\$ServiceUser cmd"
Write-Host "       $Root\tools\HorizonNET.KeyMigration.exe reprotect --db $Root\data\horizonnet.db --source-keys <transportring> --target-keys $Root\keys --target-dpapi"
Write-Host "       $Root\tools\HorizonNET.KeyMigration.exe verify --db $Root\data\horizonnet.db --keys $Root\keys"
Write-Host '  3. Transport-Ring hier UND auf dem Entwicklungsrechner löschen'
Write-Host "  4. Dienst starten:  Start-Service $ServiceName"
Write-Host "  5. Test:  Invoke-WebRequest http://localhost:$Port/api/version"
Write-Host '  6. Dann Cloudflare-Tunnel einrichten (Etappe C) und auf diesen Port zeigen lassen.'
