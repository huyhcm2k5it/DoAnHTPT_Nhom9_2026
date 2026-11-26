# Opens three counters (C1/C2/C3) on the FAKE simulated cluster. Each has its own config, login and pending store.
# Demo login: account A/B/C, password "demo". State of the fake cluster: data/demo/cluster.json (delete data/demo to reset).
param([switch] $NoBuild)
. (Join-Path $PSScriptRoot 'common.ps1')
$project = Join-Path $RepoRoot 'src/De6.Client.WinForms/De6.Client.WinForms.csproj'
Restore-TemplateProject $project
if (-not $NoBuild) {
    & $DotnetPath build $project --no-restore
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}
foreach ($id in 1..3) {
    $config = Join-Path $RepoRoot "config/local/c$id.json"
    Start-Process -FilePath $DotnetPath -WorkingDirectory $RepoRoot -ArgumentList @('run', '--project', $project, '--no-build', '--no-restore', '--', '--config', $config, '--demo')
}
