#Requires -Version 5.1
[CmdletBinding()]
param(
    [string]$AppPath,
    [string]$DataDirectory = (Join-Path $env:LOCALAPPDATA 'Gather'),
    [string]$OutputDirectory = $PSScriptRoot
)
$ErrorActionPreference = 'Stop'

function Hide-UserPath([string]$Text) {
    if ($env:USERPROFILE) { return $Text.Replace($env:USERPROFILE, '<user>') }
    return $Text
}
function Write-ReportJson([string]$Name, $Value) {
    $json = ConvertTo-Json -InputObject $Value -Depth 7
    [IO.File]::WriteAllText((Join-Path $taskReportRoot $Name), (Hide-UserPath $json), (New-Object Text.UTF8Encoding($false)))
}
function Add-AppCandidate([string]$Path) {
    if (!$Path) { return }
    if (Test-Path -LiteralPath $Path -PathType Container) { $Path = Join-Path $Path 'Gather.exe' }
    if ((Split-Path $Path -Leaf) -ieq 'Gather.exe' -and (Test-Path -LiteralPath $Path -PathType Leaf)) {
        [void]$taskAppPaths.Add([IO.Path]::GetFullPath($Path))
    }
}

if ($AppPath -and !(Test-Path -LiteralPath $AppPath)) { throw 'The supplied Gather file or folder does not exist.' }
$taskOutputRoot = [IO.Path]::GetFullPath($OutputDirectory)
$taskReportRoot = Join-Path $taskOutputRoot ('Gather-Diagnostic-Report-' + (Get-Date -Format 'yyyyMMdd-HHmmss') + '-' + [guid]::NewGuid().ToString('N').Substring(0, 6))
New-Item -ItemType Directory -Path $taskReportRoot | Out-Null
$taskAppPaths = New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::OrdinalIgnoreCase)
Add-AppCandidate $AppPath
Add-AppCandidate $PSScriptRoot
Add-AppCandidate (Join-Path $env:LOCALAPPDATA 'Programs/Gather/Gather.exe')

$taskRunning = @()
foreach ($taskProcess in @(Get-Process -Name Gather -ErrorAction SilentlyContinue)) {
    try {
        Add-AppCandidate $taskProcess.Path
        $taskBrowser = @($taskProcess.Modules | Where-Object ModuleName -EQ 'EmbeddedBrowserWebView.dll' | ForEach-Object {
            [ordered]@{ Path = Hide-UserPath $_.FileName; Version = $_.FileVersionInfo.FileVersion }
        })
        $taskRunning += [ordered]@{ Version = $taskProcess.MainModule.FileVersionInfo.FileVersion; Path = Hide-UserPath $taskProcess.Path; WorkingSetBytes = $taskProcess.WorkingSet64; PrivateBytes = $taskProcess.PrivateMemorySize64; LoadedBrowser = $taskBrowser }
    }
    catch { $taskRunning += @{ ReadError = $_.Exception.GetType().Name } }
}

$taskCrashes = @()
$taskEventReadStatus = 'Read successfully'
try {
    $taskWindowsEvents = @(Get-WinEvent -FilterHashtable @{ LogName = 'Application'; Id = @(1000, 1001, 1026); StartTime = (Get-Date).AddDays(-30) } -MaxEvents 5000 -ErrorAction Stop)
    foreach ($taskEvent in $taskWindowsEvents) {
        $taskXml = [xml]$taskEvent.ToXml()
        $taskFields = @{}
        foreach ($taskField in $taskXml.Event.EventData.Data) {
            if ($taskField.Name) { $taskFields[$taskField.Name] = [string]$taskField.'#text' }
        }
        if ($taskEvent.Id -eq 1000 -and $taskFields['AppName'] -ine 'Gather.exe') { continue }
        if ($taskEvent.Id -ne 1000 -and $taskEvent.Message -notmatch '(?i)\bGather\.exe\b') { continue }
        $taskSafeFields = [ordered]@{}
        foreach ($taskName in @('AppName','AppVersion','AppPath','ModuleName','ModuleVersion','ModulePath','ExceptionCode','FaultingOffset','EventName','P1','P2','P3','P4','P5','P6','P7','P8')) {
            if ($taskFields.ContainsKey($taskName)) { $taskSafeFields[$taskName] = Hide-UserPath $taskFields[$taskName] }
        }
        if ($taskEvent.Id -eq 1026) {
            # Capture exception types/codes, never arbitrary exception messages.
            $taskSafeFields['ExceptionTypes'] = @([regex]::Matches($taskEvent.Message, '\b[A-Za-z_][A-Za-z0-9_.]*Exception\b') | ForEach-Object Value | Select-Object -Unique)
            $taskSafeFields['ErrorCodes'] = @([regex]::Matches($taskEvent.Message, '\b0x[0-9a-fA-F]{8}\b') | ForEach-Object Value | Select-Object -Unique)
        }
        Add-AppCandidate $taskFields['AppPath']
        $taskCrashes += [ordered]@{ TimeUtc = $taskEvent.TimeCreated.ToUniversalTime().ToString('O'); EventId = $taskEvent.Id; Fields = $taskSafeFields }
    }
}
catch { $taskEventReadStatus = 'Unavailable or no matching Windows events: ' + $_.Exception.GetType().Name }

$taskRuntimeVersions = @()
foreach ($taskBase in @(${env:ProgramFiles(x86)}, $env:ProgramFiles, $env:LOCALAPPDATA) | Select-Object -Unique) {
    if (!$taskBase) { continue }
    $taskRuntimeRoot = Join-Path $taskBase 'Microsoft/EdgeWebView/Application'
    if (Test-Path -LiteralPath $taskRuntimeRoot) {
        $taskRuntimeVersions += @(Get-ChildItem -LiteralPath $taskRuntimeRoot -Directory | Where-Object { $_.Name -match '^\d+\.\d+\.\d+\.\d+$' } | ForEach-Object {
            [ordered]@{ Version = $_.Name; Location = Hide-UserPath $_.FullName }
        })
    }
}

$taskApps = @()
foreach ($taskApp in $taskAppPaths) {
    $taskFiles = @()
    foreach ($taskName in @('Gather.exe','Gather.dll','Gather.Core.dll','Gather.Signal.dll','Microsoft.UI.Xaml.dll','Microsoft.Web.WebView2.Core.dll')) {
        $taskFile = Join-Path (Split-Path $taskApp -Parent) $taskName
        if (Test-Path -LiteralPath $taskFile -PathType Leaf) {
            $taskInfo = Get-Item -LiteralPath $taskFile
            $taskFiles += [ordered]@{ Name = $taskName; Version = $taskInfo.VersionInfo.FileVersion; Bytes = $taskInfo.Length; SHA256 = (Get-FileHash -LiteralPath $taskFile -Algorithm SHA256).Hash }
        }
    }
    $taskApps += [ordered]@{ Path = Hide-UserPath $taskApp; Files = $taskFiles }
}

$taskLogs = @()
foreach ($taskName in @('startup.log','diagnostics.log')) {
    $taskLog = Join-Path $DataDirectory $taskName
    if (Test-Path -LiteralPath $taskLog -PathType Leaf) {
        # Existing Gather logs contain diagnostic stages/types/codes, not messages.
        $taskLines = @(Get-Content -LiteralPath $taskLog -Tail 350 | ForEach-Object { Hide-UserPath $_ })
        [IO.File]::WriteAllLines((Join-Path $taskReportRoot $taskName), [string[]]$taskLines, (New-Object Text.UTF8Encoding($false)))
        $taskLogs += $taskName
    }
}
$taskSummary = [ordered]@{
    CollectedUtc = [DateTimeOffset]::UtcNow.ToString('O')
    WindowsVersion = [Environment]::OSVersion.Version.ToString()
    Is64BitWindows = [Environment]::Is64BitOperatingSystem
    PowerShellVersion = $PSVersionTable.PSVersion.ToString()
    EventReadStatus = $taskEventReadStatus
    MatchingWindowsEvents = $taskCrashes.Count
    LogsIncluded = $taskLogs
    BrowserRuntimeOverridePresent = [bool]$env:WEBVIEW2_BROWSER_EXECUTABLE_FOLDER
    BrowserArgumentsOverridePresent = [bool]$env:WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS
    InstalledBrowserRuntimes = $taskRuntimeVersions
    RunningGather = $taskRunning
    GatherCopies = $taskApps
}
Write-ReportJson 'system-and-versions.json' $taskSummary
Write-ReportJson 'gather-windows-errors.json' $taskCrashes
[IO.File]::WriteAllText((Join-Path $taskReportRoot 'CONTENTS.txt'), 'This report contains Gather diagnostic logs, Gather-only Windows crash metadata, and app/runtime versions and file hashes. It does not contain chats, cookies, account settings or memory dumps. No information was uploaded. Review these files before sharing.')
$taskZip = $taskReportRoot + '.zip'
Compress-Archive -LiteralPath (Get-ChildItem -LiteralPath $taskReportRoot -File | Select-Object -ExpandProperty FullName) -DestinationPath $taskZip
Write-Host ''
Write-Host 'Gather diagnostic report saved. Send this ZIP back in the conversation:'
Write-Host $taskZip
