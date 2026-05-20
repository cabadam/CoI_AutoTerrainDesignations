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
} catch {
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
} else {
    Join-Path ${env:ProgramFiles(x86)} 'Steam\steamapps\common\Captain of Industry\Captain of Industry_Data\Managed'
}

$outputRoot = Join-Path $env:APPDATA 'Captain of Industry\Mafi'

if (-not (Test-Path -LiteralPath $managedPath)) {
    Write-Error "CoI Managed directory not found: $managedPath`nSet CAPTAIN_INDUSTRY_MANAGED_PATH to override."
    exit 1
}

New-Item -ItemType Directory -Path $outputRoot -Force | Out-Null

# ---------------------------------------------------------------------------
# 3. DLL list
# ---------------------------------------------------------------------------
$dlls = @('Mafi', 'Mafi.Core', 'Mafi.Base', 'Mafi.Unity')

# ---------------------------------------------------------------------------
# 4. Decompile loop (with change detection)
# ---------------------------------------------------------------------------
$skipped  = @()
$decompiled = @()

foreach ($name in $dlls) {
    $dllPath   = Join-Path $managedPath "$name.dll"
    $outputDir = Join-Path $outputRoot $name

    if (-not (Test-Path -LiteralPath $dllPath)) {
        Write-Warning "DLL not found, skipping: $dllPath"
        continue
    }

    if (-not $Force -and (Test-Path -LiteralPath $outputDir)) {
        $dllTime    = (Get-Item -LiteralPath $dllPath).LastWriteTime
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
# 6. Sync max_verified_game_version in manifest.json
# ---------------------------------------------------------------------------
$mafiDll = Join-Path $managedPath 'Mafi.dll'
if (Test-Path -LiteralPath $mafiDll) {
    $vi     = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($mafiDll)
    $priv   = $vi.FilePrivatePart
    $letter = if ($priv -gt 0) { [char](96 + $priv) } else { '' }
    $gameVersion = "$($vi.FileMajorPart).$($vi.FileMinorPart).$($vi.FileBuildPart)$letter"

    $manifestPath = Join-Path $PSScriptRoot '..\manifest.json'
    if (Test-Path -LiteralPath $manifestPath) {
        $manifestContent = Get-Content -LiteralPath $manifestPath -Raw
        $current = if ($manifestContent -match '"max_verified_game_version"\s*:\s*"([^"]*)"') { $Matches[1] } else { $null }

        if ($current -ne $gameVersion) {
            $updated = $manifestContent -replace '("max_verified_game_version"\s*:\s*")[^"]*"', "`${1}$gameVersion`""
            Set-Content -LiteralPath $manifestPath -Value $updated -NoNewline
            Write-Host ''
            Write-Host "manifest.json: max_verified_game_version  $current  ->  $gameVersion"
        } else {
            Write-Host ''
            Write-Host "manifest.json: max_verified_game_version already $gameVersion"
        }
    }
}
