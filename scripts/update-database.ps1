<#
.SYNOPSIS
    Wendet alle ausstehenden EF-Core-Migrationen auf die Datenbank an.

.DESCRIPTION
    Kapselt den 'dotnet ef database update'-Befehl mit den korrekten Projektpfaden.
    Hinweis: Die API wendet Migrationen beim Start ohnehin automatisch an (Migrate()).
    Dieses Skript ist nützlich, um die DB ohne App-Start zu aktualisieren.

.PARAMETER Target
    Optional: Name einer bestimmten Migration, auf die migriert werden soll
    (z.B. zum Zurückrollen auf einen früheren Stand).

.EXAMPLE
    .\scripts\update-database.ps1

.EXAMPLE
    .\scripts\update-database.ps1 InitialCreate
#>
param(
    [Parameter(Position = 0)]
    [string]$Target
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot

$efArgs = @(
    '--project',         "$repoRoot\Server\HorizonNET.Data",
    '--startup-project', "$repoRoot\Server\HorizonNET.Api"
)

if ($Target) {
    dotnet ef database update $Target @efArgs
}
else {
    dotnet ef database update @efArgs
}
