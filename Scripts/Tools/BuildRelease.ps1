param(
    [string]$Version
)

$ErrorActionPreference = "Stop"

function Get-NextVersion {
    param(
        [string]$CurrentVersion
    )

    if ($CurrentVersion -notmatch "^v(\d+)\.(\d+)$") {
        throw "当前版本号格式非法：$CurrentVersion。期望格式如 v1.11"
    }

    $major = [int]$Matches[1]
    $minor = [int]$Matches[2] + 1
    return ("v{0}.{1}" -f $major, $minor.ToString("00"))
}

function Assert-NoNewLineInField {
    param(
        [string]$FieldName,
        [string]$Value
    )

    if ([string]::IsNullOrEmpty($Value)) {
        return
    }

    if ($Value.Contains("`r") -or $Value.Contains("`n")) {
        throw "DualRoleAdventure.json 字段 '$FieldName' 不允许包含换行。请改为单行文本（不要使用 \n）。"
    }
}

$projectRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
$dllPath = Join-Path $projectRoot "DualRoleAdventure.dll"
$jsonPath = Join-Path $projectRoot "DualRoleAdventure.json"
$releaseRoot = Join-Path $projectRoot "release"

if (!(Test-Path -LiteralPath $dllPath)) {
    throw "未找到发布 DLL：$dllPath"
}

if (!(Test-Path -LiteralPath $jsonPath)) {
    throw "未找到发布 JSON：$jsonPath"
}

$rawJson = Get-Content -LiteralPath $jsonPath -Raw -Encoding UTF8
$modJson = $rawJson | ConvertFrom-Json

if ([string]::IsNullOrWhiteSpace($Version)) {
    $targetVersion = Get-NextVersion $modJson.version
}
else {
    if ($Version -notmatch "^v\d+\.\d+$") {
        throw "传入版本号格式非法：$Version。期望格式如 v1.12"
    }

    $targetVersion = $Version
}

Assert-NoNewLineInField "description" ([string]$modJson.description)
Assert-NoNewLineInField "detail" ([string]$modJson.detail)

$modJson.version = $targetVersion
$updatedJson = $modJson | ConvertTo-Json -Depth 20
Set-Content -LiteralPath $jsonPath -Value $updatedJson -Encoding UTF8

if (!(Test-Path -LiteralPath $releaseRoot)) {
    New-Item -ItemType Directory -Path $releaseRoot | Out-Null
}

$releaseName = "DualRoleAdventure-$targetVersion"
$releaseDir = Join-Path $releaseRoot $releaseName
$zipPath = Join-Path $releaseRoot "$releaseName.zip"

if (Test-Path -LiteralPath $releaseDir) {
    Remove-Item -LiteralPath $releaseDir -Recurse -Force
}

if (Test-Path -LiteralPath $zipPath) {
    Remove-Item -LiteralPath $zipPath -Force
}

New-Item -ItemType Directory -Path $releaseDir | Out-Null
Copy-Item -LiteralPath $dllPath -Destination (Join-Path $releaseDir "DualRoleAdventure.dll") -Force
Copy-Item -LiteralPath $jsonPath -Destination (Join-Path $releaseDir "DualRoleAdventure.json") -Force

Compress-Archive -Path (Join-Path $releaseDir "*") -DestinationPath $zipPath -Force

Write-Host "Release 打包完成：$releaseDir"
Write-Host "Zip 打包完成：$zipPath"
Write-Host "版本号已更新为：$targetVersion"
