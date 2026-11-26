param([Parameter(Mandatory)][ValidateSet(1, 2, 3)][int] $NodeId, [switch] $Check)
. (Join-Path $PSScriptRoot 'common.ps1')
$project = Join-Path $RepoRoot 'src/De6.Server.Host/De6.Server.Host.csproj'
$config = Join-Path $RepoRoot "config/local/s$NodeId.json"
Restore-TemplateProject $project
$appArgs = @('--config', $config)
if ($Check) { $appArgs += '--check' }
& $DotnetPath run --project $project --no-restore -- @appArgs
exit $LASTEXITCODE
