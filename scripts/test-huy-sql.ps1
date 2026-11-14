param([ValidateSet('Debug','Release')][string] $Configuration='Release',[string] $SqlServer='lpc:.\SQLEXPRESS')
. (Join-Path $PSScriptRoot 'common.ps1')
$project=Join-Path $RepoRoot 'tests/integration/De6.Booking.Persistence.Tests/De6.Booking.Persistence.Tests.csproj'
Restore-TemplateProject $project
$env:DE6_TEST_SQL_SERVER=$SqlServer
& $DotnetPath run --project $project -c $Configuration --no-restore
exit $LASTEXITCODE
