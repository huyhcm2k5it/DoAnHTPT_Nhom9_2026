param(
    [ValidateSet(1,2,3)][int] $NodeId=1,
    [string] $Database='de_6',
    [string] $SqlServer='lpc:.\SQLEXPRESS'
)
. (Join-Path $PSScriptRoot 'common.ps1')
$config=Get-Content -Raw -LiteralPath (Join-Path $RepoRoot "config/local/s$NodeId.json") | ConvertFrom-Json
$project=Join-Path $RepoRoot 'tools/De6.Sql.Tool/De6.Sql.Tool.csproj'
Restore-TemplateProject $project
& $DotnetPath run --project $project -c Release --no-restore -- init --database $Database --node $NodeId --dataset $config.datasetId --server $SqlServer --create
exit $LASTEXITCODE
