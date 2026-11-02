#Requires -Version 7.0
param([Parameter(Mandatory=$true)][string]$PackagePath, [string]$ReportDirectory, [switch]$RequireSignalRuntime)
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot 'SignalRuntime.Validation.ps1')
$taskPackageRoot=(Resolve-Path -LiteralPath $PackagePath).Path
if (!(Test-Path -LiteralPath (Join-Path $taskPackageRoot 'Gather.Signal.dll'))) { throw 'Missing native Signal integration assembly.' }
foreach ($logo in (Get-Content -LiteralPath (Join-Path $taskPackageRoot 'Notices\logo-hashes.json') -Raw | ConvertFrom-Json)) {
    if ((Get-FileHash -LiteralPath (Join-Path $taskPackageRoot ('Assets\Services\' + $logo.File)) -Algorithm SHA256).Hash -ne $logo.Hash) { throw ('Service logo hash mismatch: ' + $logo.File) }
}
if ($RequireSignalRuntime) {
    $signalRuntimeRoot = Join-Path $taskPackageRoot 'runtimes\signal'
    Assert-SignalRuntime -Directory $signalRuntimeRoot
}
foreach($taskRequiredFile in @('Gather.exe','Gather.pri','App.xbf','MainWindow.xbf','Assets\Gather.ico','Assets\Services\whatsapp.png','Assets\Services\telegram.png','Assets\Services\slack.png','Assets\Services\messenger.png','Assets\Services\discord.png','Assets\Services\signal.png','Notices\brands.md')) {
    if(!(Test-Path -LiteralPath (Join-Path $taskPackageRoot $taskRequiredFile))) { throw "Missing packaged resource: $taskRequiredFile" }
}
if(!$ReportDirectory) { $ReportDirectory=Join-Path ([System.IO.Path]::GetTempPath()) ('Gather-package-check-'+[guid]::NewGuid().ToString('N')) }
$taskReportRoot=[System.IO.Path]::GetFullPath($ReportDirectory)
if(Test-Path -LiteralPath $taskReportRoot) { throw 'Use a new empty report directory so no existing accounts are opened.' }
New-Item -ItemType Directory -Path $taskReportRoot | Out-Null
$taskChild=Start-Process -FilePath (Join-Path $taskPackageRoot 'Gather.exe') -ArgumentList @('--smoke-test','--data-dir',('"'+$taskReportRoot+'"')) -WindowStyle Hidden -PassThru
if(!$taskChild.WaitForExit(30000)) { $taskChild.Kill(); throw 'Packaged application did not complete its empty-shell check within 30 seconds.' }
if($taskChild.ExitCode -ne 0) { throw "Packaged application exited with $($taskChild.ExitCode). See startup.log in $taskReportRoot" }
$taskReport=Get-Content -LiteralPath (Join-Path $taskReportRoot 'smoke-report.json') -Raw | ConvertFrom-Json
if($taskReport.accounts -ne 0 -or $taskReport.views -ne 0) { throw 'Package check unexpectedly opened service accounts.' }
if(!(Test-Path -LiteralPath (Join-Path $taskReportRoot 'shell.png'))) { throw 'Packaged interface did not render a capture.' }
Write-Output "PASS: Packaged application launched, rendered its interface and exited successfully. Report: $taskReportRoot"

# Exercise normal startup as well, with an empty isolated profile and the same
# installed executable. Synthetic visual checks alone missed the rc.1 crash.
$taskNormalRoot=Join-Path $taskReportRoot 'normal-startup'
$taskNormalChild=Start-Process -FilePath (Join-Path $taskPackageRoot 'Gather.exe') -ArgumentList @('--startup-check','--data-dir',('"'+$taskNormalRoot+'"')) -WindowStyle Hidden -PassThru
if(!$taskNormalChild.WaitForExit(30000)) { $taskNormalChild.Kill(); throw 'Normal startup did not finish within 30 seconds.' }
if($taskNormalChild.ExitCode -ne 0) { throw "Normal startup exited with $($taskNormalChild.ExitCode). See startup.log in $taskNormalRoot" }
$taskNormalReport=Get-Content -LiteralPath (Join-Path $taskNormalRoot 'startup-report.json') -Raw | ConvertFrom-Json
if(!$taskNormalReport.ready -or $taskNormalReport.accounts -ne 0 -or $taskNormalReport.views -ne 0) { throw 'Normal startup report is incomplete or opened unexpected accounts.' }
if(!(Test-Path -LiteralPath (Join-Path $taskNormalRoot 'startup-shell.png'))) { throw 'Normal startup did not render its interface.' }
if($taskNormalReport.notificationBackend -ne 'windows-tray') { throw 'Unexpected notification backend; review its startup dependencies before shipping.' }
Write-Output "PASS: Normal startup completed and exited successfully using Windows tray notifications. Report: $taskNormalRoot"
