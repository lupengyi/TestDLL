param(
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$sourceRoot = Join-Path $repoRoot "ManualCanDebug\ManualCanDebug\bin\$Configuration"
$packageRoot = Join-Path $repoRoot "artifacts\FCT-Machine-x86"
$debugRoot = Join-Path $packageRoot "DebugTool"
$platformRoot = Join-Path $packageRoot "PlatformPatch"

if (-not (Test-Path -LiteralPath (Join-Path $sourceRoot "ManualCanDebug.exe"))) {
    throw "Release output is missing: $sourceRoot"
}

if (Test-Path -LiteralPath $packageRoot) {
    $resolvedPackage = [System.IO.Path]::GetFullPath($packageRoot)
    $resolvedArtifacts = [System.IO.Path]::GetFullPath((Join-Path $repoRoot "artifacts"))
    if (-not $resolvedPackage.StartsWith($resolvedArtifacts, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to replace package outside artifacts: $resolvedPackage"
    }
    Remove-Item -LiteralPath $packageRoot -Recurse -Force
}

New-Item -ItemType Directory -Path $debugRoot -Force | Out-Null
New-Item -ItemType Directory -Path (Join-Path $platformRoot "DLLs") -Force | Out-Null
New-Item -ItemType Directory -Path (Join-Path $platformRoot "Config") -Force | Out-Null

$excludeDirectories = @("Logs", "DebugSequences", "ErrorTrace", "StudioProjects")
Get-ChildItem -LiteralPath $sourceRoot -Force | Where-Object { $excludeDirectories -notcontains $_.Name } | ForEach-Object {
    Copy-Item -LiteralPath $_.FullName -Destination $debugRoot -Recurse -Force
}

Get-ChildItem -LiteralPath (Join-Path $sourceRoot "LegacyRuntime") -Force | ForEach-Object {
    Copy-Item -LiteralPath $_.FullName -Destination (Join-Path $platformRoot "DLLs") -Recurse -Force
}
Get-ChildItem -LiteralPath (Join-Path $sourceRoot "Config") -Force | ForEach-Object {
    Copy-Item -LiteralPath $_.FullName -Destination (Join-Path $platformRoot "Config") -Recurse -Force
}

$manifest = Get-ChildItem -LiteralPath $packageRoot -File -Recurse | ForEach-Object {
    [pscustomobject]@{
        Path = $_.FullName.Substring($packageRoot.Length + 1)
        Length = $_.Length
        SHA256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash
    }
}
$manifest | ConvertTo-Json -Depth 3 | Set-Content -LiteralPath (Join-Path $packageRoot "manifest.json") -Encoding UTF8

$zipPath = "$packageRoot.zip"
if (Test-Path -LiteralPath $zipPath) { Remove-Item -LiteralPath $zipPath -Force }
Compress-Archive -Path (Join-Path $packageRoot "*") -DestinationPath $zipPath -CompressionLevel Optimal
Write-Host "Package: $zipPath"
