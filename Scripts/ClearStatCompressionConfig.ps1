param(
    [string]$ConfigRoot = (Join-Path $env:USERPROFILE 'AppData\LocalLow\Ludeon Studios\RimWorld by Ludeon Studios\Config')
)

$ErrorActionPreference = 'Stop'

$RunningRimWorld = Get-Process -ErrorAction SilentlyContinue |
    Where-Object { $_.ProcessName -eq 'RimWorldWin64' -or $_.ProcessName -eq 'RimWorld' } |
    Select-Object -First 1
if ($null -ne $RunningRimWorld) {
    throw "RimWorld is running (PID $($RunningRimWorld.Id)). Exit the game before clearing Stat Compression settings."
}

$ResolvedConfigRoot = [System.IO.Path]::GetFullPath($ConfigRoot).TrimEnd('\')
$ExpectedSuffix = 'Ludeon Studios\RimWorld by Ludeon Studios\Config'
if (-not $ResolvedConfigRoot.EndsWith($ExpectedSuffix, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "Refusing to clear an unexpected config directory: $ResolvedConfigRoot"
}

$SettingsFile = Join-Path $ResolvedConfigRoot 'Mod_StatCompression_StatCompressionMod.xml'
$ExportDirectory = Join-Path $ResolvedConfigRoot 'StatCompression'
$Targets = @($SettingsFile, $ExportDirectory)

foreach ($Target in $Targets) {
    $ResolvedTarget = [System.IO.Path]::GetFullPath($Target)
    $ExpectedPrefix = $ResolvedConfigRoot + '\'
    if (-not $ResolvedTarget.StartsWith($ExpectedPrefix, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to delete a path outside the RimWorld config directory: $ResolvedTarget"
    }
}

if (Test-Path -LiteralPath $SettingsFile -PathType Leaf) {
    Remove-Item -LiteralPath $SettingsFile -Force
    Write-Host "Deleted ModSettings: $SettingsFile"
}
else {
    Write-Host "Not found: $SettingsFile"
}

if (Test-Path -LiteralPath $ExportDirectory -PathType Container) {
    Remove-Item -LiteralPath $ExportDirectory -Recurse -Force
    Write-Host "Deleted export/import directory: $ExportDirectory"
}
else {
    Write-Host "Not found: $ExportDirectory"
}

Write-Host 'Stat Compression configuration cleared. The next game launch will initialize settings from the bundled defaults.'
