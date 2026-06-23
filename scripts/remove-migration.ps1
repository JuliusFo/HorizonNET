<#
.SYNOPSIS
    Entfernt die letzte EF-Core-Migration.

.DESCRIPTION
    Kapselt den 'dotnet ef migrations remove'-Befehl mit den korrekten Projektpfaden.
    Nützlich, wenn eine gerade erstellte Migration noch NICHT auf die DB angewendet
    wurde und korrigiert werden soll. Bereits angewendete Migrationen sollten stattdessen
    per 'update-database.ps1 <vorherige-Migration>' zurückgerollt werden.

.EXAMPLE
    .\scripts\remove-migration.ps1
#>

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot

dotnet ef migrations remove `
    --project    "$repoRoot\Server\HorizonNET.Data" `
    --startup-project "$repoRoot\Server\HorizonNET.Api"
