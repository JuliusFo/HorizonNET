<#
.SYNOPSIS
    Erstellt eine neue EF-Core-Migration für den AppDbContext.

.DESCRIPTION
    Kapselt den 'dotnet ef migrations add'-Befehl mit den korrekten Projektpfaden.
    Die Migration wird im Data-Projekt unter Migrations/ abgelegt.

.PARAMETER Name
    Der Name der Migration, z.B. "AddTaskTags".

.EXAMPLE
    .\scripts\add-migration.ps1 AddTaskTags
#>
param(
    [Parameter(Mandatory = $true, Position = 0)]
    [string]$Name
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot

dotnet ef migrations add $Name `
    --project    "$repoRoot\Server\HorizonNET.Data" `
    --startup-project "$repoRoot\Server\HorizonNET.Api"
