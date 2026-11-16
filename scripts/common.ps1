$ErrorActionPreference = 'Stop'
$RepoRoot = Split-Path -Parent $PSScriptRoot
$localDotnet = Join-Path $RepoRoot '.tools/dotnet/dotnet.exe'
$installedDotnet = Get-Command dotnet -ErrorAction SilentlyContinue
if ($installedDotnet) {
    $DotnetPath = $installedDotnet.Source
} elseif (Test-Path -LiteralPath $localDotnet) {
    $DotnetPath = $localDotnet
} else {
    throw 'Install the .NET 10 SDK on PATH or extract it into .tools/dotnet.'
}
$env:DOTNET_CLI_HOME = Join-Path $RepoRoot '.tools/dotnet-home'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
$TemplateNugetConfig = Join-Path $PSScriptRoot 'nuget-template.config'

function Restore-TemplateProject([string] $Project) {
    if ($env:DE6_NUGET_SOURCE) {
        & $DotnetPath restore $Project --source $env:DE6_NUGET_SOURCE
    } else {
        & $DotnetPath restore $Project --configfile $TemplateNugetConfig
    }
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}
