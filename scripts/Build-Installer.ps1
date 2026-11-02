#Requires -Version 7.0
param([string]$OutputRoot, [string]$InnoCompiler)
$ErrorActionPreference = 'Stop'
$sourceRoot = Split-Path $PSScriptRoot -Parent
$workRoot = Split-Path $sourceRoot -Parent
$workspaceRoot = Split-Path $workRoot -Parent
$buildEnvironment = Join-Path $workRoot 'tooling/build-env.ps1'
if (Test-Path -LiteralPath $buildEnvironment) { . $buildEnvironment }
if (!$OutputRoot) { $OutputRoot = Join-Path $workspaceRoot 'outputs' }
if (!$InnoCompiler) { $InnoCompiler = Join-Path $workRoot 'tooling/inno/ISCC.exe' }
if (!(Test-Path -LiteralPath $InnoCompiler)) { throw 'Pass -InnoCompiler with the full path to ISCC.exe.' }
[xml]$project = Get-Content -LiteralPath (Join-Path $sourceRoot 'Gather.App/Gather.App.csproj') -Raw
$version = $project.SelectSingleNode('/Project/PropertyGroup/Version').InnerText
$fileVersion = $project.SelectSingleNode('/Project/PropertyGroup/FileVersion').InnerText
if ($version -notmatch '^\d+\.\d+\.\d+(?:-[a-zA-Z0-9.-]+)?$') { throw 'Invalid project version.' }
$installerScript = Join-Path $PSScriptRoot 'Gather.iss'
$installerText = Get-Content -LiteralPath $installerScript -Raw
foreach ($expected in @("AppVersion=$version", "VersionInfoVersion=$fileVersion", "OutputBaseFilename=Gather-Setup-$version")) {
    if ($installerText -notmatch ('(?m)^'+[regex]::Escape($expected)+'\r?$')) { throw ('Installer metadata must match the project: '+$expected) }
}
$package = Join-Path $OutputRoot ('releases/Gather-Signal-Development-'+$version)
$installers = Join-Path $OutputRoot 'installers'
$documents = Join-Path $OutputRoot 'docs'
$logRoot = Join-Path $workRoot 'logs'
$qaRoot = Join-Path $workRoot ('qa/installer-'+$version+'-'+[guid]::NewGuid().ToString('N'))
$env:TEMP = Join-Path $workRoot 'temp'
$env:TMP = $env:TEMP
foreach ($directory in @($installers,$documents,$logRoot,$qaRoot,$env:TEMP)) { New-Item -ItemType Directory -Force -Path $directory | Out-Null }
Push-Location $sourceRoot
try {
    Write-Output ('Publishing '+$version)
    dotnet publish Gather.App/Gather.App.csproj -c Release --no-restore -o $package -m:1 -nr:false -p:BuildInParallel=false -p:UseSharedCompilation=false *> (Join-Path $logRoot ($version+'-publish.log'))
    if ($LASTEXITCODE -ne 0) { throw 'Publish failed; see the publish log.' }
    & (Join-Path $PSScriptRoot 'Verify-Package.ps1') -PackagePath $package -ReportDirectory (Join-Path $qaRoot 'published') -RequireSignalRuntime
    Write-Output 'Compiling the production installer.'
    & $InnoCompiler '/Q' ('/DPackageRoot='+$package) ('/DReleaseOutput='+$installers) $installerScript *> (Join-Path $logRoot ($version+'-installer-build.log'))
    if ($LASTEXITCODE -ne 0) { throw 'Installer compilation failed; see its build log.' }
    Write-Output 'Testing an isolated QA installation of the same file payload.'
    & $InnoCompiler '/Q' '/DQA' ('/DPackageRoot='+$package) ('/DReleaseOutput='+$qaRoot) $installerScript *> (Join-Path $logRoot ($version+'-installer-qa-build.log'))
    if ($LASTEXITCODE -ne 0) { throw 'QA installer compilation failed.' }
    $installed = Join-Path $qaRoot 'installed'
    $setupLog = Join-Path $qaRoot 'setup.log'
    $setup = Start-Process -FilePath (Join-Path $qaRoot 'Gather-Setup-QA.exe') -ArgumentList @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART',('/DIR="'+$installed+'"'),('/LOG="'+$setupLog+'"')) -WindowStyle Hidden -PassThru
    if (!$setup.WaitForExit(55000)) { throw 'QA installation did not complete within 55 seconds.' }
    if ($setup.ExitCode -ne 0) { throw ('QA installer failed: '+$setup.ExitCode+'. See '+$setupLog) }
    & (Join-Path $PSScriptRoot 'Verify-Package.ps1') -PackagePath $installed -ReportDirectory (Join-Path $qaRoot 'installed-check') -RequireSignalRuntime
    $installer = Get-Item -LiteralPath (Join-Path $installers ('Gather-Setup-'+$version+'.exe'))
    if ($installer.VersionInfo.FileVersion.Trim() -ne $fileVersion) { throw 'Installer file version does not match the project.' }
    $sha = (Get-FileHash -LiteralPath $installer.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
    $sha+'  '+$installer.Name | Set-Content -LiteralPath (Join-Path $documents ('Gather-SHA256SUMS-'+$version+'.txt'))
    $result = [pscustomobject]@{version=$version;installer=$installer.FullName;package=$package;sha256=$sha;qaDirectory=$qaRoot;qaInstallExit=0;runtimeAndStartupChecksPassed=$true}
    $result | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $documents ('Gather-installer-verification-'+$version+'.json'))
    $result | ConvertTo-Json
}
finally { Pop-Location }
