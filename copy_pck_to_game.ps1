$ErrorActionPreference = "Stop"

$modName = "DualRoleAdventure"
$targetDir = "E:\SteamLibrary\steamapps\common\Slay the Spire 2\mods\$modName"
$godotExePath = "E:\Godot_v4.5.1-stable_mono_win64\Godot_v4.5.1-stable_mono_win64.exe"
$exportPreset = "Windows Desktop"
$exportPckPath = Join-Path $PSScriptRoot "$modName.pck"

if (-not (Test-Path -LiteralPath $godotExePath)) {
    throw "Godot executable not found: $godotExePath"
}

Write-Host "Exporting pck: $exportPckPath"
& $godotExePath --path $PSScriptRoot --export-pack $exportPreset $exportPckPath
if ($LASTEXITCODE -ne 0) {
    throw "Godot export-pack failed with exit code: $LASTEXITCODE"
}

$pckCandidates = @(
    (Join-Path $PSScriptRoot "$modName.pck"),
    (Join-Path $PSScriptRoot "LocalMultiControl.pck")
)

$sourcePckPath = $pckCandidates | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
$sourceDllPath = Join-Path $PSScriptRoot "$modName.dll"

if (-not $sourcePckPath) {
    throw "Source pck not found. Tried: $($pckCandidates -join ', '). Export pck first."
}

if (-not (Test-Path -LiteralPath $sourceDllPath)) {
    throw "Source dll not found: $sourceDllPath. Build project first to generate dll in project root."
}

if (-not (Test-Path -LiteralPath $targetDir)) {
    New-Item -ItemType Directory -Path $targetDir -Force | Out-Null
}

Copy-Item -LiteralPath $sourcePckPath -Destination (Join-Path $targetDir "$modName.pck") -Force
Copy-Item -LiteralPath $sourceDllPath -Destination (Join-Path $targetDir "$modName.dll") -Force

Write-Host "Copied pck: $sourcePckPath -> $(Join-Path $targetDir "$modName.pck")"
Write-Host "Copied dll: $sourceDllPath -> $(Join-Path $targetDir "$modName.dll")"
