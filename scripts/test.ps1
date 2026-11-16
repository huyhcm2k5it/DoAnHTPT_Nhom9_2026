param([ValidateSet('Debug', 'Release')][string] $Configuration = 'Debug')
. (Join-Path $PSScriptRoot 'common.ps1')
& (Join-Path $PSScriptRoot 'build.ps1') -Configuration $Configuration
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
$harness = Join-Path $RepoRoot "tests/De6.Template.SmokeTests/bin/$Configuration/net10.0/De6.Template.SmokeTests.dll"
& $DotnetPath $harness
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
$contracts=Join-Path $RepoRoot "tests/unit/De6.Persistence.Contracts.Tests/bin/$Configuration/net10.0/De6.Persistence.Contracts.Tests.dll"
& $DotnetPath $contracts
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
$hostChecks=Join-Path $RepoRoot "tests/unit/De6.Host.Persistence.Tests/bin/$Configuration/net10.0/De6.Host.Persistence.Tests.dll"
& $DotnetPath $hostChecks
exit $LASTEXITCODE
