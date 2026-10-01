param([string]$SettingsPath, [switch]$Rebuild)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$project = Join-Path $projectRoot 'src\VinsaStudio\VinsaStudio.csproj'
$publishFolder = Join-Path $projectRoot 'build\VinsaStudio'
$executable = Join-Path $publishFolder 'VinsaStudio.exe'
if (-not $SettingsPath) {
    $installed = Join-Path ${env:ProgramFiles(x86)} 'MyTablet\TSetting.ini'
    if (Test-Path -LiteralPath $installed) {
        $SettingsPath = $installed
    } else {
        $SettingsPath = Join-Path $projectRoot 'downloads\windows\installed\MyTablet\TSetting.ini'
    }
}
$settingsFile = (Resolve-Path -LiteralPath $SettingsPath).Path
if ($Rebuild -or -not (Test-Path -LiteralPath $executable)) {
    $dotnet = (Get-Command dotnet.exe -ErrorAction Stop).Source
    & $dotnet publish $project -c Release -o $publishFolder --nologo
    if ($LASTEXITCODE -ne 0) { throw 'Vinsa Studio build failed.' }
}
Start-Process -FilePath $executable -ArgumentList @(
    '--settings', ('"{0}"' -f $settingsFile)
) -WorkingDirectory $projectRoot -WindowStyle Normal
