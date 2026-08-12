param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',

    [string]$RimWorldPath = 'E:\Program Files\Steam\steamapps\common\RimWorld',
    [string]$HarmonyModPath = 'E:\Program Files\Steam\steamapps\workshop\content\294100\2009463077',
    [switch]$IncludePdb
)

$ErrorActionPreference = 'Stop'

$RepoRoot = Split-Path -Parent $PSScriptRoot
$ProjectPath = Join-Path $RepoRoot 'Source\StatCompression\StatCompression.csproj'
$StageRoot = Join-Path $RepoRoot 'Build\StatCompression'
$GameManagedPath = Join-Path $RimWorldPath 'RimWorldWin64_Data\Managed'
$HarmonyAssembliesPath = Join-Path $HarmonyModPath 'Current\Assemblies'

function Assert-PathExists {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path,

        [Parameter(Mandatory = $true)]
        [string]$Label
    )

    if (-not (Test-Path -LiteralPath $Path)) {
        throw "$Label not found: $Path"
    }
}

Assert-PathExists -Path $ProjectPath -Label 'Project file'
Assert-PathExists -Path (Join-Path $GameManagedPath 'Assembly-CSharp.dll') -Label 'RimWorld Assembly-CSharp.dll'
Assert-PathExists -Path (Join-Path $HarmonyAssembliesPath '0Harmony.dll') -Label 'Harmony 0Harmony.dll'

dotnet build $ProjectPath `
    --configuration $Configuration `
    /p:RimWorldPath="$RimWorldPath" `
    /p:HarmonyModPath="$HarmonyModPath"

if ($LASTEXITCODE -ne 0) {
    throw "dotnet build failed with exit code $LASTEXITCODE"
}

$ResolvedRepoRoot = [System.IO.Path]::GetFullPath($RepoRoot)
$ResolvedStageRoot = [System.IO.Path]::GetFullPath($StageRoot)
if (-not $ResolvedStageRoot.StartsWith($ResolvedRepoRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "Refusing to clean staging path outside repository: $ResolvedStageRoot"
}

if (Test-Path -LiteralPath $StageRoot) {
    Remove-Item -LiteralPath $StageRoot -Recurse -Force
}

New-Item -ItemType Directory -Force -Path (Join-Path $StageRoot 'About') | Out-Null
New-Item -ItemType Directory -Force -Path (Join-Path $StageRoot 'Defs') | Out-Null
New-Item -ItemType Directory -Force -Path (Join-Path $StageRoot 'Data') | Out-Null
New-Item -ItemType Directory -Force -Path (Join-Path $StageRoot 'Languages') | Out-Null
New-Item -ItemType Directory -Force -Path (Join-Path $StageRoot '1.6\Assemblies') | Out-Null

Copy-Item -LiteralPath (Join-Path $RepoRoot 'About\About.xml') -Destination (Join-Path $StageRoot 'About\About.xml') -Force
Copy-Item -LiteralPath (Join-Path $RepoRoot 'About\LoadFolders.xml') -Destination (Join-Path $StageRoot 'About\LoadFolders.xml') -Force
$DefsPath = Join-Path $RepoRoot 'Defs'
if (Test-Path -LiteralPath $DefsPath) {
    Copy-Item -LiteralPath $DefsPath -Destination $StageRoot -Recurse -Force
}
Copy-Item -LiteralPath (Join-Path $RepoRoot 'Data') -Destination $StageRoot -Recurse -Force
Copy-Item -LiteralPath (Join-Path $RepoRoot 'Languages') -Destination $StageRoot -Recurse -Force

$BuildOutput = Join-Path $RepoRoot "Source\StatCompression\bin\$Configuration"
Copy-Item -LiteralPath (Join-Path $BuildOutput 'StatCompression.dll') -Destination (Join-Path $StageRoot '1.6\Assemblies\StatCompression.dll') -Force

if ($IncludePdb) {
    $PdbPath = Join-Path $BuildOutput 'StatCompression.pdb'
    if (Test-Path -LiteralPath $PdbPath) {
        Copy-Item -LiteralPath $PdbPath -Destination (Join-Path $StageRoot '1.6\Assemblies\StatCompression.pdb') -Force
    }
}

Write-Host "Built mod package: $StageRoot"
