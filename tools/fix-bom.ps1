param([Parameter(Mandatory=$true)][string]$Path)

$bytes = [IO.File]::ReadAllBytes($Path)
$hasBom = $bytes.Length -ge 3 -and $bytes[0] -eq 0xEF -and $bytes[1] -eq 0xBB -and $bytes[2] -eq 0xBF
if ($hasBom) {
    Write-Output "ALREADY-BOM $Path"
    exit 0
}
$text = [Text.Encoding]::UTF8.GetString($bytes)
[IO.File]::WriteAllText($Path, $text, (New-Object Text.UTF8Encoding($true)))
Write-Output "BOM-ADDED $Path"
