<#
.SYNOPSIS
    Convenience wrapper around reduce_fbx.py — finds Blender and runs it headless.

.EXAMPLE
    .\Reduce-Fbx.ps1 ..\Assets\3DModels\old-generator-game-asset\model.fbx -Ratio 0.4

.EXAMPLE
    .\Reduce-Fbx.ps1 ..\Assets\3DModels -Ratio 0.5            # batch a folder

.EXAMPLE
    .\Reduce-Fbx.ps1 model.fbx -TargetFaces 5000 -Triangulate

.NOTES
    Set $env:BLENDER to override auto-detection, e.g.
        $env:BLENDER = "C:\Program Files\Blender Foundation\Blender 4.2\blender.exe"
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true, Position = 0)]
    [string]$Input,

    [Parameter(Position = 1)]
    [string]$Output,

    [double]$Ratio = 0.5,
    [int]$TargetFaces = 0,
    [ValidateSet("collapse", "unsubdiv", "planar")]
    [string]$Mode = "collapse",
    [int]$Iterations = 2,
    [double]$Angle = 5.0,
    [switch]$Triangulate,
    [switch]$KeepNormals,
    [double]$Scale = 1.0,
    [switch]$DryRun
)

$ErrorActionPreference = "Stop"

function Find-Blender {
    if ($env:BLENDER -and (Test-Path $env:BLENDER)) { return $env:BLENDER }

    $cmd = Get-Command blender -ErrorAction SilentlyContinue
    if ($cmd) { return $cmd.Source }

    $candidates = @()
    foreach ($root in @("$env:ProgramFiles\Blender Foundation",
                        "${env:ProgramFiles(x86)}\Blender Foundation")) {
        if (Test-Path $root) {
            $candidates += Get-ChildItem $root -Filter blender.exe -Recurse -ErrorAction SilentlyContinue
        }
    }
    # Steam install fallback
    $steam = "$env:ProgramFiles(x86)\Steam\steamapps\common\Blender\blender.exe"
    if (Test-Path $steam) { $candidates += Get-Item $steam }

    if ($candidates.Count -gt 0) {
        return ($candidates | Sort-Object FullName -Descending | Select-Object -First 1).FullName
    }
    return $null
}

$blender = Find-Blender
if (-not $blender) {
    Write-Error "Blender not found. Install it, add it to PATH, or set `$env:BLENDER to blender.exe."
    exit 1
}
Write-Host "Using Blender: $blender" -ForegroundColor Cyan

$script = Join-Path $PSScriptRoot "reduce_fbx.py"

# Build the argument list passed after "--" to the Python script.
$pyArgs = @($Input)
if ($Output) { $pyArgs += $Output }
if ($TargetFaces -gt 0) { $pyArgs += @("--target-faces", $TargetFaces) }
else { $pyArgs += @("--ratio", $Ratio) }
$pyArgs += @("--mode", $Mode)
if ($Mode -eq "unsubdiv") { $pyArgs += @("--iterations", $Iterations) }
if ($Mode -eq "planar") { $pyArgs += @("--angle", $Angle) }
if ($Triangulate) { $pyArgs += "--triangulate" }
if ($KeepNormals) { $pyArgs += "--keep-normals" }
$pyArgs += @("--scale", $Scale)
if ($DryRun) { $pyArgs += "--dry-run" }

& $blender --background --python $script -- @pyArgs
exit $LASTEXITCODE
