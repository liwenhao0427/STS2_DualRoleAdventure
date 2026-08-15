param(
    [string]$Sts2Dir = $env:STS2_GAME_DIR
)

$ErrorActionPreference = "Stop"

$modName = "DualRoleAdventure"
$sourceDllPath = Join-Path $PSScriptRoot "$modName.dll"
$sourceJsonPath = Join-Path $PSScriptRoot "$modName.json"

if ([string]::IsNullOrWhiteSpace($Sts2Dir)) {
    $steamRoots = @(
        (Get-ItemProperty -Path "HKCU:\Software\Valve\Steam" -ErrorAction SilentlyContinue).SteamPath,
        (Get-ItemProperty -Path "HKLM:\SOFTWARE\WOW6432Node\Valve\Steam" -ErrorAction SilentlyContinue).InstallPath,
        (Join-Path ${env:ProgramFiles(x86)} "Steam")
    ) | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | Select-Object -Unique

    foreach ($steamRoot in $steamRoots) {
        $candidate = Join-Path $steamRoot "steamapps\common\Slay the Spire 2"
        if (Test-Path -LiteralPath (Join-Path $candidate "data_sts2_windows_x86_64\sts2.dll")) {
            $Sts2Dir = $candidate
            break
        }
    }
}

if ([string]::IsNullOrWhiteSpace($Sts2Dir) -or
    -not (Test-Path -LiteralPath (Join-Path $Sts2Dir "data_sts2_windows_x86_64\sts2.dll"))) {
    throw "Slay the Spire 2 installation not found. Pass -Sts2Dir or set STS2_GAME_DIR."
}

$targetDir = Join-Path $Sts2Dir "mods\$modName"

if (-not (Test-Path -LiteralPath $sourceDllPath)) {
    throw "Source dll not found: $sourceDllPath. Build project first to generate dll in project root."
}

if (-not (Test-Path -LiteralPath $sourceJsonPath)) {
    throw "Source json not found: $sourceJsonPath. Ensure release config json exists in project root."
}

if (-not (Test-Path -LiteralPath $targetDir)) {
    New-Item -ItemType Directory -Path $targetDir -Force | Out-Null
}

Copy-Item -LiteralPath $sourceDllPath -Destination (Join-Path $targetDir "$modName.dll") -Force
Copy-Item -LiteralPath $sourceJsonPath -Destination (Join-Path $targetDir "$modName.json") -Force

Write-Host "Using game directory: $Sts2Dir"
Write-Host "Copied dll: $sourceDllPath -> $(Join-Path $targetDir "$modName.dll")"
Write-Host "Copied json: $sourceJsonPath -> $(Join-Path $targetDir "$modName.json")"
