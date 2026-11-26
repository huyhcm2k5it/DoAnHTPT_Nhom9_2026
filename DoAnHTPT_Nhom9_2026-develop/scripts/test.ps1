param([ValidateSet('Debug', 'Release')][string] $Configuration = 'Debug')
. (Join-Path $PSScriptRoot 'common.ps1')
& (Join-Path $PSScriptRoot 'build.ps1') -Configuration $Configuration
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

$smokeHarness = Join-Path $RepoRoot "tests/De6.Template.SmokeTests/bin/$Configuration/net10.0/De6.Template.SmokeTests.dll"
& $DotnetPath $smokeHarness
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

$domainHarness = Join-Path $RepoRoot "tests/unit/De6.Domain.UnitTests/bin/$Configuration/net10.0/De6.Domain.UnitTests.dll"
& $DotnetPath $domainHarness
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

$transportHarness = Join-Path $RepoRoot "tests/unit/De6.Transport.Tcp.Tests/bin/$Configuration/net10.0/De6.Transport.Tcp.Tests.dll"
& $DotnetPath $transportHarness
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

# K6 controller tests: real client SDK against the FAKE simulated cluster (not T-matrix evidence).
$k6Harness = Join-Path $RepoRoot "tests/unit/De6.Client.K6.Tests/bin/$Configuration/net10.0/De6.Client.K6.Tests.dll"
& $DotnetPath $k6Harness
exit $LASTEXITCODE
