param([Parameter(Mandatory)][ValidateSet(1, 2, 3)][int] $ClientId, [switch] $Check, [switch] $Demo)
. (Join-Path $PSScriptRoot 'common.ps1')
$project = Join-Path $RepoRoot 'src/De6.Client.WinForms/De6.Client.WinForms.csproj'
$config = Join-Path $RepoRoot "config/local/c$ClientId.json"
Restore-TemplateProject $project
$appArgs = @('--config', $config)
if ($Check) { $appArgs += '--check' }
if ($Demo) { $appArgs += '--demo' }
& $DotnetPath run --project $project --no-restore -- @appArgs
exit $LASTEXITCODE
