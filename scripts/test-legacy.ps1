param(
    [Parameter(Mandatory)]
    [string] $LegacySourceRoot
)

$ErrorActionPreference = 'Stop'
$expected = '0716ee7262eff2c268ebb72097ceb93e0fc83536'
$root = (Resolve-Path -LiteralPath $LegacySourceRoot).Path
$actual = & git -C $root rev-parse HEAD
if ($LASTEXITCODE -ne 0 -or $actual -ne $expected) {
    throw "Legacy checkout must be at commit $expected."
}
$changed = & git -C $root status --porcelain --untracked-files=no
if ($LASTEXITCODE -ne 0 -or $changed) {
    throw 'Legacy checkout has tracked modifications or could not be inspected.'
}
$project = Join-Path $PSScriptRoot '..\tests\Contoso.Students.LegacyCharacterization\Contoso.Students.LegacyCharacterization.csproj'
& dotnet restore $project --locked-mode
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
& dotnet test $project --no-restore "-p:LegacySourceRoot=$root"
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
