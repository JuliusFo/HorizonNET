<#
.SYNOPSIS
    Sichert die HorizonNET-Datenbank samt DataProtection-Schlüsselring.

.DESCRIPTION
    Legt unter backups\ einen Zeitstempel-Ordner an mit:
      - horizonnet.db (plus -wal/-shm, falls vorhanden)
      - dem DataProtection-Schlüsselring (Standard: %LOCALAPPDATA%\HorizonNET\keys)

    Beides gehört zusammen: Ohne den Schlüsselring lassen sich die verschlüsselten
    Spalten (Google-Token, ab Phase 12c die Journal-Texte) nicht mehr entschlüsseln –
    eine Sicherung nur der DB wäre für diese Inhalte wertlos.

    WICHTIG: Genau deshalb sollte die fertige Sicherung NICHT auf derselben Platte
    liegen bleiben. Und wer den Ordner in fremde Hände gibt, gibt beides zugleich weg –
    für eine Ablage außer Haus den Ordner vorher in ein Archiv mit Passwort packen.

    Die API sollte beim Sichern gestoppt sein. Läuft sie, kann eine offene WAL-Datei
    Änderungen enthalten, die noch nicht in der .db stehen; das Skript warnt dann.

.PARAMETER Destination
    Optional: Zielordner. Standard ist <repo>\backups.

.EXAMPLE
    .\scripts\backup-database.ps1

.EXAMPLE
    .\scripts\backup-database.ps1 -Destination D:\Sicherungen\HorizonNET
#>
param(
    [string]$Destination
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot

$dbPath = Join-Path $repoRoot 'Server\HorizonNET.Api\horizonnet.db'
if (-not (Test-Path $dbPath)) {
    throw "Datenbank nicht gefunden: $dbPath"
}

# Läuft die API noch? Dann ist die Kopie möglicherweise nicht konsistent.
$listening = Get-NetTCPConnection -LocalPort 7012, 5081 -State Listen -ErrorAction SilentlyContinue
if ($listening) {
    Write-Warning 'Die API scheint zu laufen (Port 7012/5081). Fuer eine konsistente Sicherung bitte stoppen.'
}

if (-not $Destination) { $Destination = Join-Path $repoRoot 'backups' }
$stamp = Get-Date -Format 'yyyy-MM-dd_HHmm'
$target = Join-Path $Destination $stamp
New-Item -ItemType Directory -Path $target -Force | Out-Null

# ── Datenbank (inkl. WAL/SHM, falls die App sie gerade nutzt) ──────────────────
Copy-Item -Path $dbPath -Destination $target
foreach ($suffix in '-wal', '-shm') {
    $side = "$dbPath$suffix"
    if (Test-Path $side) { Copy-Item -Path $side -Destination $target }
}

# ── DataProtection-Schlüsselring ──────────────────────────────────────────────
# Pfad wie in Program.cs: DataProtection:KeyRingPath, sonst %LOCALAPPDATA%\HorizonNET\keys.
$keyRing = $env:HORIZONNET_KEYRING
if (-not $keyRing) { $keyRing = Join-Path $env:LOCALAPPDATA 'HorizonNET\keys' }

if (Test-Path $keyRing) {
    $keyTarget = Join-Path $target 'keys'
    New-Item -ItemType Directory -Path $keyTarget -Force | Out-Null
    Copy-Item -Path (Join-Path $keyRing '*') -Destination $keyTarget -Recurse
    $keyCount = (Get-ChildItem $keyTarget -File -Recurse).Count
}
else {
    Write-Warning "Schluesselring nicht gefunden unter '$keyRing' - die Sicherung enthaelt NUR die Datenbank. Verschluesselte Spalten waeren daraus nicht wiederherstellbar."
    $keyCount = 0
}

# ── Nachweis statt gutem Glauben: Hashes vergleichen ──────────────────────────
$copiedDb = Join-Path $target (Split-Path $dbPath -Leaf)
$sourceHash = (Get-FileHash $dbPath -Algorithm SHA256).Hash
$targetHash = (Get-FileHash $copiedDb -Algorithm SHA256).Hash
if ($sourceHash -ne $targetHash) {
    throw "Pruefsumme der Kopie weicht ab - Sicherung NICHT verwendbar: $copiedDb"
}

[PSCustomObject]@{
    Ordner        = $target
    DatenbankKB   = [math]::Round((Get-Item $copiedDb).Length / 1KB, 1)
    Schluesseldateien = $keyCount
    HashGeprueft  = $true
} | Format-List

Write-Host 'Hinweis: Diese Sicherung enthaelt Datenbank UND Schluessel. Fuer eine Ablage ausser Haus vorher verschluesselt verpacken.' -ForegroundColor Yellow
