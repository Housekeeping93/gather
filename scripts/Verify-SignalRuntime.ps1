#Requires -Version 7.0
param([Parameter(Mandatory=$true)][string]$FixtureDirectory)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'SignalRuntime.Validation.ps1')
# Use fixtures exported by Gather.Tests --runtime-fixtures. Only validate file
# inventories; never execute any file inside these synthetic distributions.
$cases = @(Get-Content -LiteralPath (Join-Path $FixtureDirectory 'expectations.json') -Raw | ConvertFrom-Json)
if ($cases.Count -eq 0) { throw 'No runtime validation cases supplied.' }
foreach ($case in $cases) {
    $valid = $true
    try { Assert-SignalRuntime -Directory $case.directory } catch { $valid = $false }
    if ($valid -ne $case.valid) { throw ('Unexpected package runtime validation result: ' + $case.name) }
    Write-Output ('PASS runtime package inventory: ' + $case.name)
}
