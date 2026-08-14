param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',

    [string]$RimWorldPath = 'E:\Program Files\Steam\steamapps\common\RimWorld',
    [string]$HarmonyModPath = 'E:\Program Files\Steam\steamapps\workshop\content\294100\2009463077',
    [string]$RimWorldModsPath = 'E:\Program Files\Steam\steamapps\common\RimWorld\Mods',
    [string]$ModFolderName = 'StatCompression',
    [switch]$IncludePdb
)

$ErrorActionPreference = 'Stop'

$RepoRoot = Split-Path -Parent $PSScriptRoot
$BuildScript = Join-Path $PSScriptRoot 'Build.ps1'
$StageRoot = Join-Path $RepoRoot 'Build\StatCompression'
$DeployRoot = Join-Path $RimWorldModsPath $ModFolderName

& $BuildScript `
    -Configuration $Configuration `
    -RimWorldPath $RimWorldPath `
    -HarmonyModPath $HarmonyModPath `
    -IncludePdb:$IncludePdb

if ($LASTEXITCODE -ne 0) {
    throw "Build script failed with exit code $LASTEXITCODE"
}

$ResolvedModsPath = [System.IO.Path]::GetFullPath($RimWorldModsPath)
$ResolvedDeployRoot = [System.IO.Path]::GetFullPath($DeployRoot)
if (-not $ResolvedDeployRoot.StartsWith($ResolvedModsPath, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "Refusing to deploy outside RimWorld Mods path: $ResolvedDeployRoot"
}

$PublishedFileIdBackups = @()
if (Test-Path -LiteralPath $DeployRoot) {
    Get-ChildItem -LiteralPath $DeployRoot -Recurse -File -Force -Filter 'PublishedFileId.txt' |
        ForEach-Object {
            $relativePath = $_.FullName.Substring($ResolvedDeployRoot.Length).TrimStart('\')
            $PublishedFileIdBackups += [PSCustomObject]@{
                RelativePath = $relativePath
                Content = [System.IO.File]::ReadAllBytes($_.FullName)
            }
        }
}

try {
    if (Test-Path -LiteralPath $DeployRoot) {
        Get-ChildItem -LiteralPath $DeployRoot -Recurse -File -Force |
            Remove-Item -Force

        Get-ChildItem -LiteralPath $DeployRoot -Recurse -Directory -Force |
            Sort-Object { $_.FullName.Length } -Descending |
            Where-Object { (Get-ChildItem -LiteralPath $_.FullName -Force).Count -eq 0 } |
            Remove-Item -Force
    }

    New-Item -ItemType Directory -Force -Path $DeployRoot | Out-Null
    Copy-Item -Path (Join-Path $StageRoot '*') -Destination $DeployRoot -Recurse -Force
}
finally {
    foreach ($Backup in $PublishedFileIdBackups) {
        $RestorePath = Join-Path $DeployRoot $Backup.RelativePath
        $RestoreDirectory = Split-Path -Parent $RestorePath
        New-Item -ItemType Directory -Force -Path $RestoreDirectory | Out-Null
        $RestoreRequired = $true
        if (Test-Path -LiteralPath $RestorePath -PathType Leaf) {
            $CurrentContent = [System.IO.File]::ReadAllBytes($RestorePath)
            $RestoreRequired = [System.Convert]::ToBase64String($CurrentContent) -ne
                [System.Convert]::ToBase64String($Backup.Content)
        }

        if ($RestoreRequired) {
            Remove-Item -LiteralPath $RestorePath -Force -ErrorAction SilentlyContinue
            [System.IO.File]::WriteAllBytes($RestorePath, $Backup.Content)
        }
    }
}

Write-Host "Deployed mod package: $DeployRoot"
