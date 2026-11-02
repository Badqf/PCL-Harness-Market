#requires -Version 5.1
<#
.SYNOPSIS
  从 GitHub Releases 汇总插件包下载量，生成 downloads.json。

.DESCRIPTION
  GitHub 只为 Release 资产（assets[].download_count）提供公开计数，
  仓库里的 index/packages/*.pclplugin 与 raw 直链不计入。

  脚本读取仓库全部 Release（分页），把文件名符合
  <plugin-id>-<version>.pclplugin 的资产按插件 id 归组并累加下载量，
  输出 downloads.json；make-index.ps1 会把它合并进根目录 index.json。

  需要 GITHUB_TOKEN（只读 public_repo 即可）；未提供时读取环境变量。

.EXAMPLE
  $env:GITHUB_TOKEN | Out-Null
  pwsh tools/update-downloads.ps1 -Root . -Repo Badqf/PCL-Harness-Market
#>
[CmdletBinding()]
param(
    [string]$Root = '',
    [string]$Repo = 'Badqf/PCL-Harness-Market',
    [string]$Token = $env:GITHUB_TOKEN,
    [string]$Output = 'downloads.json',
    [switch]$DryRun,
    [string]$ApiBase = 'https://api.github.com'
)

if ([string]::IsNullOrWhiteSpace($Root)) { $Root = Split-Path $PSScriptRoot -Parent }
$ErrorActionPreference = 'Stop'

function Fail([string]$Message) {
    Write-Host "ERROR: $Message" -ForegroundColor Red
    exit 1
}

if ([string]::IsNullOrWhiteSpace($Token)) {
    Fail '缺少 GITHUB_TOKEN；请设置环境变量或在 -Token 传入。'
}
if ($Repo -cnotmatch '^[A-Za-z0-9_.\-]+/[A-Za-z0-9_.\-]+$') {
    Fail "Repo 格式应为 owner/name：$Repo"
}

$headers = @{
    Authorization          = "Bearer $Token"
    Accept                 = 'application/vnd.github+json'
    'User-Agent'           = 'PCL-Harness-Market-Updater'
    'X-GitHub-Api-Version' = '2022-11-28'
}

$assetPattern = '^(?<id>[a-z0-9][a-z0-9_\-\.]{0,63})-(?<version>\d+(?:\.\d+){1,3}(?:-[0-9A-Za-z.\-]+)?)\.pclplugin$'
$totals = @{}
$releases = New-Object System.Collections.ArrayList
$page = 1
while ($true) {
    $uri = ($ApiBase.TrimEnd('/') + "/repos/$Repo/releases?per_page=100&page=$page")
    try {
        $batch = @(Invoke-RestMethod -Uri $uri -Headers $headers -Method Get)
    }
    catch {
        Fail "读取 Releases 失败（第 $page 页）：$($_.Exception.Message)"
    }
    if ($batch.Count -eq 0) { break }
    foreach ($rel in $batch) { [void]$releases.Add($rel) }
    if ($batch.Count -lt 100) { break }
    $page++
    if ($page -gt 20) { Fail 'Release 分页超过 20 页，停止以避免失控。' }
}

foreach ($rel in $releases) {
    foreach ($asset in @($rel.assets)) {
        if ($null -eq $asset -or [string]::IsNullOrWhiteSpace([string]$asset.name)) { continue }
        $m = [regex]::Match([string]$asset.name, $assetPattern)
        if (-not $m.Success) { continue }
        $id = $m.Groups['id'].Value
        $count = 0L
        if (-not [long]::TryParse([string]$asset.download_count, [ref]$count)) { $count = 0 }
        if (-not $totals.ContainsKey($id)) { $totals[$id] = 0L }
        $totals[$id] = [long]$totals[$id] + $count
    }
}

$updatedAt = (Get-Date).ToUniversalTime().ToString('yyyy-MM-ddTHH:mm:ssZ')
$result = [ordered]@{}
foreach ($id in ($totals.Keys | Sort-Object)) {
    $result[$id] = [ordered]@{
        total     = [long]$totals[$id]
        updatedAt = $updatedAt
        source    = 'github-release'
    }
}

$json = $result | ConvertTo-Json -Depth 6
$outputFull = [System.IO.Path]::GetFullPath((Join-Path $Root $Output))
if ($DryRun) {
    Write-Host $json
    Write-Host "DRY-RUN: $($totals.Count) 个插件、$($releases.Count) 个 Release，未写文件。" -ForegroundColor Yellow
    exit 0
}
[System.IO.File]::WriteAllText($outputFull, $json + "`r`n", (New-Object System.Text.UTF8Encoding($false)))
Write-Host "OK: 已写入 $outputFull（$($totals.Count) 个插件、$($releases.Count) 个 Release）" -ForegroundColor Green
