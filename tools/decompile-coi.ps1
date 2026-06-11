# decompile-coi.ps1
# Decompiles the Mafi DLLs from the CoI installation into the standard decompiled-source directory.
#
# Prerequisites:
#   dotnet tool install ilspycmd -g
#
# Usage:
#   .\tools\decompile-coi.ps1            # skip DLLs whose output is already newer than the source DLL
#   .\tools\decompile-coi.ps1 -Force     # always re-decompile all DLLs

param(
    [switch]$Force
)

$ErrorActionPreference = 'Stop'

# ---------------------------------------------------------------------------
# 1. Prerequisite check
# ---------------------------------------------------------------------------
$ilspy = $null
try {
    $ilspy = (Get-Command ilspycmd -ErrorAction Stop).Source
}
catch {
    Write-Error @'
ilspycmd is not installed. Install it once with:

    dotnet tool install ilspycmd -g

Then re-run this script.
'@
    exit 1
}

# ---------------------------------------------------------------------------
# 2. Resolve paths
# ---------------------------------------------------------------------------
$managedPath = if ($env:CAPTAIN_INDUSTRY_MANAGED_PATH) {
    $env:CAPTAIN_INDUSTRY_MANAGED_PATH
}
else {
    Join-Path ${env:ProgramFiles(x86)} 'Steam\steamapps\common\Captain of Industry\Captain of Industry_Data\Managed'
}

$outputRoot = Join-Path $env:APPDATA 'Captain of Industry\Mafi'

if (-not (Test-Path -LiteralPath $managedPath)) {
    Write-Error "CoI Managed directory not found: $managedPath`nSet CAPTAIN_INDUSTRY_MANAGED_PATH to override."
    exit 1
}

New-Item -ItemType Directory -Path $outputRoot -Force | Out-Null

# ---------------------------------------------------------------------------
# 2.5. Read game version from DLL and build number from decompiled source
# ---------------------------------------------------------------------------
$gameVersion = $null
$mafiDllPath = Join-Path $managedPath 'Mafi.dll'
if (Test-Path -LiteralPath $mafiDllPath) {
    $vi = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($mafiDllPath)
    $priv = $vi.FilePrivatePart
    $letter = if ($priv -gt 0) { [char](96 + $priv) } else { '' }
    $gameVersion = "$($vi.FileMajorPart).$($vi.FileMinorPart).$($vi.FileBuildPart)$letter"
}

# Build number is a uint literal in GameVersion.cs: string.Format("... (b{2})", ..., NNNu).AsLoc()
$buildNumber = $null
$gameVersionCsPath = Join-Path $outputRoot 'Mafi\Mafi\GameVersion.cs'
if (Test-Path -LiteralPath $gameVersionCsPath) {
    $gvContent = Get-Content -LiteralPath $gameVersionCsPath -Raw
    if ($gvContent -match ',\s*(\d+)u\)\.AsLoc\(\)') { $buildNumber = $Matches[1] }
}

# ---------------------------------------------------------------------------
# 2.6. Git pre-snapshot — commit current state before wiping
# ---------------------------------------------------------------------------
$useGit = Test-Path -LiteralPath (Join-Path $outputRoot '.git')
if ($useGit) {
    Push-Location $outputRoot
    $dirty = git status --porcelain 2>&1
    if ($dirty) {
        Write-Host '[git] Committing current decompiled state before update...'
        git add -A | Out-Null
        $snapLabel = if ($buildNumber) { "$gameVersion-b$buildNumber" } elseif ($gameVersion) { $gameVersion } else { 'unknown' }
        $snapMsg = "CoI $snapLabel"
        git commit -m $snapMsg --quiet
    }
    Pop-Location
}

# ---------------------------------------------------------------------------
# 3. DLL list
# ---------------------------------------------------------------------------
$dlls = @('Mafi', 'Mafi.Core', 'Mafi.Base', 'Mafi.Unity')

# ---------------------------------------------------------------------------
# 4. Decompile loop (with change detection)
# ---------------------------------------------------------------------------
$skipped = @()
$decompiled = @()

foreach ($name in $dlls) {
    $dllPath = Join-Path $managedPath "$name.dll"
    $outputDir = Join-Path $outputRoot $name

    if (-not (Test-Path -LiteralPath $dllPath)) {
        Write-Warning "DLL not found, skipping: $dllPath"
        continue
    }

    if (-not $Force -and (Test-Path -LiteralPath $outputDir)) {
        $dllTime = (Get-Item -LiteralPath $dllPath).LastWriteTime
        $newestFile = Get-ChildItem -LiteralPath $outputDir -Recurse -File -ErrorAction SilentlyContinue |
        Sort-Object LastWriteTime -Descending |
        Select-Object -First 1

        if ($null -ne $newestFile -and $newestFile.LastWriteTime -ge $dllTime) {
            Write-Host "[skip] $name  (output is up to date)"
            $skipped += $name
            continue
        }
    }

    Write-Host "[decompile] $name ..."
    if (Test-Path -LiteralPath $outputDir) {
        Remove-Item -LiteralPath $outputDir -Recurse -Force
    }

    & $ilspy $dllPath --project --outputdir $outputDir --nested-directories
    if ($LASTEXITCODE -ne 0) {
        Write-Error "ilspycmd failed for $name (exit $LASTEXITCODE)"
        exit $LASTEXITCODE
    }

    $decompiled += $name
}

# ---------------------------------------------------------------------------
# 5. Summary
# ---------------------------------------------------------------------------
Write-Host ''
Write-Host "Output: $outputRoot"

if ($decompiled.Count -gt 0) {
    Write-Host "Decompiled : $($decompiled -join ', ')"
}
if ($skipped.Count -gt 0) {
    Write-Host "Up to date : $($skipped -join ', ')  (use -Force to re-decompile)"
}

# ---------------------------------------------------------------------------
# 5.5. Git commit — record new decompiled state and show diff
# ---------------------------------------------------------------------------
if ($useGit -and $decompiled.Count -gt 0) {
    Push-Location $outputRoot
    # Re-read build number from the freshly decompiled GameVersion.cs
    if (Test-Path -LiteralPath $gameVersionCsPath) {
        $gvContent = Get-Content -LiteralPath $gameVersionCsPath -Raw
        if ($gvContent -match ',\s*(\d+)u\)\.AsLoc\(\)') { $buildNumber = $Matches[1] }
    }
    $versionLabel = if ($gameVersion -and $buildNumber) { "$gameVersion-b$buildNumber" } elseif ($gameVersion) { $gameVersion } else { $null }
    git add -A | Out-Null
    git diff --cached --quiet 2>$null
    if ($LASTEXITCODE -ne 0) {
        $commitMsg = if ($versionLabel) { "CoI $versionLabel" } else { 'decompiled update' }
        git commit -m $commitMsg --quiet
        if ($versionLabel) { git tag "v$versionLabel" 2>$null }
        $commitCount = [int](git rev-list --count HEAD 2>$null)
        Write-Host ''
        Write-Host "[git] Committed: $commitMsg  (tag: v$versionLabel)"
        if ($commitCount -gt 1) {
            Write-Host ''
            git diff HEAD~1 --stat
        }
    }
    else {
        Write-Host ''
        Write-Host '[git] Decompiled output unchanged from last commit.'
    }
    Pop-Location
}

# ---------------------------------------------------------------------------
# 6. Sync max_verified_game_version in manifest.json
# ---------------------------------------------------------------------------
if ($null -ne $gameVersion) {
    $manifestPath = Join-Path $PSScriptRoot '..\manifest.json'
    if (Test-Path -LiteralPath $manifestPath) {
        $manifestContent = Get-Content -LiteralPath $manifestPath -Raw
        $current = if ($manifestContent -match '"max_verified_game_version"\s*:\s*"([^"]*)"') { $Matches[1] } else { $null }

        if ($current -ne $gameVersion) {
            $updated = $manifestContent -replace '("max_verified_game_version"\s*:\s*")[^"]*"', "`${1}$gameVersion`""
            Set-Content -LiteralPath $manifestPath -Value $updated -NoNewline
            Write-Host ''
            Write-Host "manifest.json: max_verified_game_version  $current  ->  $gameVersion"
        }
        else {
            Write-Host ''
            Write-Host "manifest.json: max_verified_game_version already $gameVersion"
        }
    }
}
