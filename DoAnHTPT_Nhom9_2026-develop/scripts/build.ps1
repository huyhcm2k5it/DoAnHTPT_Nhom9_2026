param([ValidateSet('Debug', 'Release')][string] $Configuration = 'Debug')
. (Join-Path $PSScriptRoot 'common.ps1')
$solution = Join-Path $RepoRoot 'DoAnHTPT_Nhom9_2026.sln'
Restore-TemplateProject $solution
& $DotnetPath build $solution --no-restore --configuration $Configuration
exit $LASTEXITCODE
