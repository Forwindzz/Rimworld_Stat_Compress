param(
    [string]$SourcePath = (Join-Path $env:USERPROFILE 'AppData\LocalLow\Ludeon Studios\RimWorld by Ludeon Studios\Config\StatCompression\settings.xml')
)

$ErrorActionPreference = 'Stop'

$RepoRoot = Split-Path -Parent $PSScriptRoot
$DestinationPath = Join-Path $RepoRoot 'Data\DefaultSettings.xml'

if (-not (Test-Path -LiteralPath $SourcePath -PathType Leaf)) {
    throw "Exported settings XML not found: $SourcePath"
}

try {
    [xml]$Document = Get-Content -LiteralPath $SourcePath -Raw
}
catch {
    throw "Failed to parse exported settings XML: $SourcePath`n$_"
}

if ($Document.DocumentElement.Name -ne 'StatCompressionSettings') {
    throw "Unexpected XML root '$($Document.DocumentElement.Name)'; expected 'StatCompressionSettings'."
}

$Stats = @($Document.StatCompressionSettings.Stats.Stat)
if ($Stats.Count -eq 0) {
    throw 'Exported settings XML contains no Stat entries.'
}

$DuplicateDefNames = @(
    $Stats |
        Group-Object -Property defName |
        Where-Object Count -gt 1 |
        Select-Object -ExpandProperty Name
)
if ($DuplicateDefNames.Count -gt 0) {
    throw "Exported settings XML contains duplicate defNames: $($DuplicateDefNames -join ', ')"
}

$DestinationDirectory = Split-Path -Parent $DestinationPath
New-Item -ItemType Directory -Force -Path $DestinationDirectory | Out-Null
Copy-Item -LiteralPath $SourcePath -Destination $DestinationPath -Force

$Hash = (Get-FileHash -LiteralPath $DestinationPath -Algorithm SHA256).Hash
Write-Host "Updated mod default settings: $DestinationPath"
Write-Host "Stats: $($Stats.Count)"
Write-Host "SHA256: $Hash"
