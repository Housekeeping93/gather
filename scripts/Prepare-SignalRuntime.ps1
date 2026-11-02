#Requires -Version 7.0
param(
    [Parameter(Mandatory=$true)][string]$SignalCliDirectory,
    [Parameter(Mandatory=$true)][string]$JavaDirectory
)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'SignalRuntime.Validation.ps1')
# Import extracted, trusted upstream distributions. This never changes system Java.
$signalSource = (Resolve-Path -LiteralPath $SignalCliDirectory).Path
$javaSource = (Resolve-Path -LiteralPath $JavaDirectory).Path
$projectRoot = Split-Path -Parent $PSScriptRoot
$destination = [IO.Path]::GetFullPath((Join-Path $projectRoot 'Gather.App\runtimes\signal'))
if (Test-Path -LiteralPath $destination) { throw 'A Signal runtime already exists. Use a fresh development checkout to replace or update it.' }
# Reject links before invoking any supplied executable or traversing its dependencies.
foreach ($source in @($signalSource, $javaSource)) { $null = @(Get-SignalRuntimeFiles -Directory $source) }
if (!(Test-Path -LiteralPath (Join-Path $signalSource 'lib\signal-cli-0.14.7.jar'))) { throw 'Expected the extracted signal-cli 0.14.7 JVM distribution.' }
$javaExecutable = Join-Path $javaSource 'bin\java.exe'
if (!(Test-Path -LiteralPath $javaExecutable)) { throw 'Expected an extracted Windows x64 Java 25 distribution.' }
$javaInfo = & $javaExecutable -version 2>&1 | Out-String
if ($LASTEXITCODE -ne 0 -or $javaInfo -notmatch 'version "25(?:\.|\+|")' -or $javaInfo -notmatch '64-Bit') { throw 'Java 25 x64 could not be verified.' }
# Checking the version does not register, link or access a Signal account.
$cliInfo = & $javaExecutable '-cp' (Join-Path $signalSource 'lib\*') 'org.asamk.signal.Main' '--version' 2>&1 | Out-String
if ($LASTEXITCODE -ne 0 -or $cliInfo -notmatch 'signal-cli 0\.14\.7(?:\s|$)') { throw 'The supplied Signal component did not report version 0.14.7.' }
$runtimeParent = Split-Path -Parent $destination
New-Item -ItemType Directory -Path $runtimeParent -Force | Out-Null
if ((Get-Item -LiteralPath $runtimeParent).Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'The application runtime directory must not be a filesystem link.' }
$staging = [IO.Path]::GetFullPath((Join-Path $runtimeParent ('.signal-staging-' + [guid]::NewGuid().ToString('N'))))
New-Item -ItemType Directory -Path $staging | Out-Null
try {
    Copy-Item -LiteralPath $signalSource -Destination (Join-Path $staging 'signal-cli') -Recurse
    Copy-Item -LiteralPath $javaSource -Destination (Join-Path $staging 'java') -Recurse
    $runtimeHashes = @(Get-SignalRuntimeFiles -Directory $staging | ForEach-Object {
        [pscustomobject]@{ File = [IO.Path]::GetRelativePath($staging, $_.FullName).Replace('\','/'); SHA256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash }
    })
    [pscustomobject]@{
        SignalCliVersion = '0.14.7'; JavaMajorVersion = 25; PreparedUtc = [DateTimeOffset]::UtcNow.ToString('O');
        SignalSource = 'https://github.com/AsamK/signal-cli/releases/tag/v0.14.7';
        Files = $runtimeHashes
    } | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $staging 'runtime-manifest.json') -Encoding utf8NoBOM
    Assert-SignalRuntime -Directory $staging
    # Check both absolute paths before the directory move; a failed import stays
    # outside runtimes/signal and can never be included by the package glob.
    if ((Split-Path -Parent $staging) -ne $runtimeParent -or (Split-Path -Parent $destination) -ne $runtimeParent -or
        (Split-Path -Leaf $staging) -notlike '.signal-staging-*' -or (Split-Path -Leaf $destination) -ne 'signal') { throw 'Unexpected runtime staging paths.' }
    if (Test-Path -LiteralPath $destination) { throw 'A Signal runtime appeared while preparing this import; it has been preserved.' }
    # Directory.Move refuses an existing destination (unlike Move-Item's nesting behavior).
    [IO.Directory]::Move($staging, $destination)
} catch {
    throw "Signal runtime import did not complete. Staged files remain at '$staging'. $($_.Exception.Message)"
}
Write-Output 'Signal runtime prepared. Rebuild the development package, then run its live-linking acceptance checks. No Signal account was accessed.'
