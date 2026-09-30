[CmdletBinding()]
param(
    [string]$ArtifactRoot = (Join-Path (Split-Path -Parent $PSScriptRoot) 'downloads'),
    [string]$OutputPath
)

$ErrorActionPreference = 'Stop'
$rootItem = Get-Item -LiteralPath $ArtifactRoot
if (-not $rootItem.PSIsContainer) {
    throw "ArtifactRoot must be a directory: $ArtifactRoot"
}
if (($rootItem.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
    throw "ArtifactRoot must not be a reparse point: $ArtifactRoot"
}

$rootPath = $rootItem.FullName.TrimEnd([IO.Path]::DirectorySeparatorChar)
$files = [Collections.Generic.List[object]]::new()
$pending = [Collections.Generic.Stack[string]]::new()
$pending.Push($rootPath)
while ($pending.Count -gt 0) {
    foreach ($item in Get-ChildItem -LiteralPath $pending.Pop() -Force) {
        if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
            throw "Inventory does not follow reparse points: $($item.FullName)"
        }
        if ($item.PSIsContainer) {
            $pending.Push($item.FullName)
            continue
        }
        $files.Add([PSCustomObject]@{
            path = $item.FullName.Substring($rootPath.Length + 1).Replace('\', '/')
            bytes = $item.Length
            sha256 = (Get-FileHash -LiteralPath $item.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
        })
    }
}

$orderedFiles = @($files | Sort-Object path)
$duplicateGroups = @($orderedFiles | Group-Object sha256 | Where-Object Count -gt 1 | ForEach-Object {
    [PSCustomObject]@{
        sha256 = $_.Name
        paths = @($_.Group.path)
    }
})
$inventory = [PSCustomObject]@{
    schema_version = 1
    files = $orderedFiles
    exact_duplicates = $duplicateGroups
}
$json = $inventory | ConvertTo-Json -Depth 6
if ($OutputPath) {
    # Require an existing destination directory, and never overwrite a file.
    $outputFullPath = [IO.Path]::GetFullPath($OutputPath)
    $stream = [IO.File]::Open($outputFullPath, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write)
    try {
        $bytes = [Text.UTF8Encoding]::new($false).GetBytes($json + [Environment]::NewLine)
        $stream.Write($bytes, 0, $bytes.Length)
    }
    finally {
        $stream.Dispose()
    }
}
else {
    $json
}
