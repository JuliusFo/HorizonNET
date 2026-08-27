<#
.SYNOPSIS
    Schnürt das Deployment-Paket für den vServer.

.DESCRIPTION
    Erstellt unter publish\ ein ZIP mit allem, was der Server braucht:
      - app\    Release-Publish der API (liefert auch den Blazor-Client aus)
      - tools\  HorizonNET.KeyMigration (für den Schlüsselring-Umzug, siehe
                docs\konzept-dpapi-umzug.md)
      - server-install.ps1 (Einrichtung auf dem Server, einmalig)

    Das ZIP auf den vServer kopieren, entpacken, dort server-install.ps1 als
    Administrator ausführen. publish\ ist git-ignoriert.

.EXAMPLE
    .\scripts\deploy-package.ps1
#>
param()

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$publishRoot = Join-Path $repoRoot 'publish'
$staging = Join-Path $publishRoot 'staging'

if (Test-Path $staging) { Remove-Item $staging -Recurse -Force }
New-Item -ItemType Directory -Force $staging | Out-Null

Write-Host 'Publish der API (Release)…'
dotnet publish (Join-Path $repoRoot 'Server\HorizonNET.Api') -c Release -o (Join-Path $staging 'app') --nologo
if ($LASTEXITCODE -ne 0) { throw 'Publish der API fehlgeschlagen.' }

Write-Host 'Publish des Umzugs-Tools…'
dotnet publish (Join-Path $repoRoot 'Tools\HorizonNET.KeyMigration') -c Release -o (Join-Path $staging 'tools') --nologo
if ($LASTEXITCODE -ne 0) { throw 'Publish des Tools fehlgeschlagen.' }

Copy-Item (Join-Path $PSScriptRoot 'server-install.ps1') $staging

# Version aus der gebauten Assembly für den ZIP-Namen
$dll = Join-Path $staging 'app\HorizonNET.Api.dll'
$version = [Diagnostics.FileVersionInfo]::GetVersionInfo($dll).FileVersion
$zip = Join-Path $publishRoot "HorizonNET-deploy-$version.zip"

if (Test-Path $zip) { Remove-Item $zip -Force }
Compress-Archive -Path (Join-Path $staging '*') -DestinationPath $zip
Remove-Item $staging -Recurse -Force

Write-Host "Fertig: $zip"
Write-Host 'Nächster Schritt: ZIP auf den vServer kopieren, entpacken, server-install.ps1 als Administrator ausführen.'
