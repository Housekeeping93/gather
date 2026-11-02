#Requires -Version 7.0
param(
    [string]$Version = '0.10.0-dev.9',
    [Parameter(Mandatory=$true)][string]$SourceCommit
)
$ErrorActionPreference = 'Stop'
$sourceRoot = Split-Path $PSScriptRoot -Parent
$workRoot = Split-Path $sourceRoot -Parent
$workspaceRoot = Split-Path $workRoot -Parent
$repository = 'Housekeeping93/gather'
if ($Version -notmatch '^\d+\.\d+\.\d+(?:-[a-zA-Z0-9.-]+)?$' -or $SourceCommit -notmatch '^[0-9a-f]{40}$') { throw 'Pass an exact version and full GitHub source commit SHA.' }
$ghCommand = Get-Command gh -ErrorAction SilentlyContinue
$gh = if ($ghCommand) { $ghCommand.Source } else { Join-Path $workRoot 'tooling/gh/2.103.3/bin/gh.exe' }
if (!(Test-Path -LiteralPath $gh)) { throw 'GitHub CLI is required.' }
& $gh auth status --hostname github.com *> $null
if ($LASTEXITCODE -ne 0) { throw 'Authorize GitHub CLI with gh auth login --web before publishing.' }
$installer = Join-Path $workspaceRoot ('outputs/installers/Gather-Setup-'+$Version+'.exe')
$checksums = Join-Path $workspaceRoot ('outputs/docs/Gather-SHA256SUMS-'+$Version+'.txt')
$notes = Join-Path $sourceRoot ('docs/releases/'+$Version+'.md')
$verification = Get-Content -LiteralPath (Join-Path $workspaceRoot ('outputs/docs/Gather-installer-verification-'+$Version+'.json')) -Raw | ConvertFrom-Json
foreach ($file in @($installer,$checksums,$notes)) { if (!(Test-Path -LiteralPath $file)) { throw ('Missing release file: '+$file) } }
if (!$verification.runtimeAndStartupChecksPassed -or $verification.qaInstallExit -ne 0) { throw 'Installer verification is incomplete.' }
$hash = (Get-FileHash -LiteralPath $installer -Algorithm SHA256).Hash.ToLowerInvariant()
if ($hash -ne $verification.sha256) { throw 'Installer differs from the verified build.' }
if ((Get-Content -LiteralPath $checksums -Raw) -notmatch [regex]::Escape($hash)) { throw 'Installer checksum file does not match.' }
$tag = 'v'+$Version
$existing = & $gh release view $tag --repo $repository --json isDraft,url,assets 2>$null
$exists = $LASTEXITCODE -eq 0
if (!$exists) {
    & $gh release create $tag --repo $repository --target $SourceCommit --title ('Gather '+$Version+' (Windows preview)') --notes-file $notes --draft --prerelease
    if ($LASTEXITCODE -ne 0) { throw 'Could not create the draft release.' }
}
$releaseApi = & $gh release view $tag --repo $repository --json apiUrl --jq .apiUrl
if ($LASTEXITCODE -ne 0 -or !$releaseApi) { throw 'Could not locate the draft release API URL.' }
foreach ($file in @($installer,$checksums)) {
    $release = & $gh api $releaseApi | ConvertFrom-Json
    if ($LASTEXITCODE -ne 0) { throw 'Could not read the release.' }
    if ($release.target_commitish -ne $SourceCommit) { throw 'The release targets a different source commit; inspect it before changing it.' }
    $name = [IO.Path]::GetFileName($file)
    $asset = $release.assets | Where-Object name -EQ $name
    if (!$asset) {
        & $gh release upload $tag $file --repo $repository
        if ($LASTEXITCODE -ne 0) { throw ('Could not upload '+$name+'. The draft remains unpublished.') }
    }
}
$release = & $gh api $releaseApi | ConvertFrom-Json
foreach ($file in @($installer,$checksums)) {
    $asset = $release.assets | Where-Object name -EQ ([IO.Path]::GetFileName($file))
    if (!$asset -or $asset.state -ne 'uploaded' -or $asset.size -ne (Get-Item -LiteralPath $file).Length) { throw 'Uploaded release asset is incomplete.' }
    $fileHash = (Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($asset.digest -and $asset.digest -ne ('sha256:'+$fileHash)) { throw 'GitHub asset digest does not match the local file.' }
}
if ($release.draft) {
    & $gh release edit $tag --repo $repository --draft=false --prerelease
    if ($LASTEXITCODE -ne 0) { throw 'Could not publish the verified draft release.' }
}
$release = & $gh api $releaseApi | ConvertFrom-Json
if ($release.draft) { throw 'Release is still a draft.' }
[pscustomobject]@{releaseUrl=$release.html_url;tag=$tag;sourceCommit=$SourceCommit;installerSha256=$hash;assets=@($release.assets | Select-Object name,size,digest,browser_download_url)} |
    ConvertTo-Json -Depth 5 | Tee-Object -FilePath (Join-Path $workspaceRoot ('outputs/docs/Gather-GitHub-release-'+$Version+'.json'))
