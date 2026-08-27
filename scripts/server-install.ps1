<#
.SYNOPSIS
    Richtet HorizonNET auf dem vServer ein (einmalig, als Administrator).

.DESCRIPTION
    Führt auf dem Server die Einrichtung aus docs\deployment-vserver.md aus:
      1. Ordnerstruktur unter -Root (app, data, keys, tools)
      2. Dediziertes lokales Dienstkonto "horizonnet" (DPAPI bindet den Schlüsselring
         an genau dieses Konto – deshalb kein LocalSystem)
      3. App- und Tool-Dateien aus dem entpackten Deployment-Paket kopieren
      4. Windows-Dienst inkl. Umgebungsvariablen (Secrets werden abgefragt, nie geloggt)

    Der Dienst wird bewusst NICHT gestartet: Erst müssen DB + Schlüsselring per
    Umzugs-Tool ankommen (docs\konzept-dpapi-umzug.md). Das Skript druckt die
    nächsten Schritte am Ende.

    Erneut ausführen ist ungefährlich: Bestehendes wird aktualisiert statt doppelt
    angelegt.

.PARAMETER Domain
    Öffentlicher Hostname hinter dem Tunnel, z. B. horizon.example.de (für AllowedHosts).

.PARAMETER Root
    Zielordner der Installation. Standard: C:\HorizonNET

.PARAMETER Port
    Lokaler HTTP-Port, auf den cloudflared zeigt. Standard: 5000

.EXAMPLE
    .\server-install.ps1 -Domain horizon.example.de
#>
param(
    [Parameter(Mandatory = $true)][string]$Domain,
    [string]$Root = 'C:\HorizonNET',
    [int]$Port = 5000,
    [string]$ServiceName = 'HorizonNET',
    [string]$ServiceUser = 'horizonnet'
)

$ErrorActionPreference = 'Stop'

if (-not ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()
        ).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw 'Bitte als Administrator ausführen.'
}

$package = $PSScriptRoot
if (-not (Test-Path (Join-Path $package 'app\HorizonNET.Api.exe'))) {
    throw "Kein Deployment-Paket gefunden – dieses Skript aus dem entpackten ZIP heraus starten (app\ fehlt neben $package)."
}

# ── 1) Ordner ────────────────────────────────────────────────────────────────
foreach ($dir in 'app', 'data', 'keys', 'tools') {
    New-Item -ItemType Directory -Force (Join-Path $Root $dir) | Out-Null
}

# ── 2) Dienstkonto ───────────────────────────────────────────────────────────
$existing = Get-LocalUser -Name $ServiceUser -ErrorAction SilentlyContinue
if ($existing) {
    Write-Host "Dienstkonto '$ServiceUser' existiert bereits – Passwort wird für die Dienstanmeldung erneut benötigt."
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

# Anmelderechte per secedit ergänzen:
#   SeServiceLogonRight – der Dienst selbst
#   SeBatchLogonRight   – geplante Aufgaben (DPAPI-Umzug läuft als Task unter diesem
#                         Konto, weil runas/Get-Credential in SSH-Sitzungen scheitern)
$sid = (Get-LocalUser -Name $ServiceUser).SID.Value
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

# Zugriff: Dienstkonto bekommt den Root-Ordner; der Schlüsselring gehört NUR ihm + Admins.
icacls $Root /grant "${ServiceUser}:(OI)(CI)M" /T /Q | Out-Null
icacls (Join-Path $Root 'keys') /inheritance:r /grant "${ServiceUser}:(OI)(CI)F" /grant 'Administratoren:(OI)(CI)F' /Q | Out-Null

# ── 3) Dateien ───────────────────────────────────────────────────────────────
Write-Host 'Kopiere App und Tools…'
Copy-Item (Join-Path $package 'app\*')   (Join-Path $Root 'app')   -Recurse -Force
Copy-Item (Join-Path $package 'tools\*') (Join-Path $Root 'tools') -Recurse -Force

# ── 4) Dienst + Umgebungsvariablen ──────────────────────────────────────────
$googleId     = Read-Host 'Google ClientId (leer = überspringen, später nachtragbar)'
$googleSecret = if ($googleId) { Read-Host 'Google ClientSecret' } else { '' }

$envVars = @(
    'ASPNETCORE_ENVIRONMENT=Production'
    "ASPNETCORE_URLS=http://localhost:$Port"
    "AllowedHosts=$Domain;localhost"
    "ConnectionStrings__DefaultConnection=Data Source=$Root\data\horizonnet.db"
    "DataProtection__KeyRingPath=$Root\keys"
)
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

# Env-Vars gelten NUR für diesen Dienst (Registry Multi-String "Environment")
Set-ItemProperty -Path "HKLM:\SYSTEM\CurrentControlSet\Services\$ServiceName" `
    -Name 'Environment' -Value $envVars -Type MultiString

Write-Host ''
Write-Host '════════════════════════════════════════════════════════════'
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
