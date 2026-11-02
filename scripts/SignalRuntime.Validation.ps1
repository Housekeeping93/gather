function Get-SignalRuntimeFiles {
    param([Parameter(Mandatory=$true)][string]$Directory)
    $runtimeRoot = (Resolve-Path -LiteralPath $Directory).Path
    if ((Get-Item -LiteralPath $runtimeRoot).Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Runtime folder must not be a filesystem link.' }
    $pendingDirectories = [Collections.Generic.Stack[string]]::new()
    $pendingDirectories.Push($runtimeRoot)
    while ($pendingDirectories.Count -gt 0) {
        foreach ($item in Get-ChildItem -LiteralPath $pendingDirectories.Pop() -Force) {
            if ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Runtime files and directories must not be filesystem links.' }
            if ($item.PSIsContainer) { $pendingDirectories.Push($item.FullName) } else { $item }
        }
    }
}

function Assert-SignalRuntime {
    param([Parameter(Mandatory=$true)][string]$Directory)
    $runtimeRoot = (Resolve-Path -LiteralPath $Directory).Path
    $actualFiles = @(Get-SignalRuntimeFiles -Directory $runtimeRoot)
    $manifestPath = Join-Path $runtimeRoot 'runtime-manifest.json'
    if (!(Test-Path -LiteralPath $manifestPath -PathType Leaf) -or (Get-Item -LiteralPath $manifestPath).Length -gt 4MB) { throw 'Missing or invalid Signal runtime manifest.' }
    $manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
    if ($manifest.SignalCliVersion -ne '0.14.7' -or $manifest.JavaMajorVersion -ne 25 -or !$manifest.Files -or @($manifest.Files).Count -eq 0) { throw 'Signal runtime versions or file inventory are invalid.' }
    $listed = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    foreach ($file in $manifest.Files) {
        if ([string]::IsNullOrWhiteSpace($file.File) -or $file.SHA256 -notmatch '^[0-9A-Fa-f]{64}$') { throw 'Invalid Signal runtime file entry.' }
        $relative = $file.File.Replace('\','/')
        if ([IO.Path]::IsPathRooted($relative) -or $relative.Contains(':') -or @($relative.Split('/') | Where-Object { $_ -in @('', '.', '..') }).Count -gt 0) { throw 'Invalid relative path in Signal runtime inventory.' }
        if (!$listed.Add($relative) -or $relative -eq 'runtime-manifest.json') { throw 'Duplicate or self-referential runtime inventory entry.' }
        $path = Join-Path $runtimeRoot $relative
        if (!(Test-Path -LiteralPath $path -PathType Leaf) -or (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -ne $file.SHA256) { throw 'A Signal runtime file is missing or has changed.' }
    }
    foreach ($required in @('java/bin/java.exe','java/bin/server/jvm.dll','java/lib/modules','java/release','signal-cli/lib/signal-cli-0.14.7.jar')) {
        if (!$listed.Contains($required)) { throw ('Missing required Signal runtime file: ' + $required) }
    }
    if (@($listed | Where-Object { $_ -like 'signal-cli/lib/libsignal-client-*.jar' }).Count -eq 0) { throw 'The native Signal dependency is missing from the runtime inventory.' }
    foreach ($file in $actualFiles) {
        if ($file.FullName -eq $manifestPath) { continue }
        if (!$listed.Contains([IO.Path]::GetRelativePath($runtimeRoot, $file.FullName).Replace('\','/'))) { throw 'Signal runtime contains a file absent from its inventory.' }
    }
}
