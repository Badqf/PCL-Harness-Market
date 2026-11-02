#requires -Version 5.1
<#
.SYNOPSIS
  校验 index/plugins/*.json 并生成仓库根目录的 index.json。

.DESCRIPTION
  每个 index/plugins/<id>.json 是一个完整的插件条目。脚本会：
    * 校验 id、版本号、下载地址、SHA-256 等字段；
    * 如果条目里写了 latest.package，就按相对路径找到本地包，
      自动补全（或校验）size 与 sha256，并在输出里移除 package 字段；
    * 按 featured 优先、id 升序排序；
    * 原子地写出 index.json（UTF-8，无 BOM）。

  加 -Check 时只校验、不写文件，适合 CI。

.EXAMPLE
  pwsh tools/make-index.ps1
  pwsh tools/make-index.ps1 -Check
#>
[CmdletBinding()]
param(
    [string]$Root = '',
    [string]$SourceDir = 'index/plugins',
    [string]$Output = 'index.json',
    [switch]$Check
)

if ([string]::IsNullOrWhiteSpace($Root)) { $Root = Split-Path $PSScriptRoot -Parent }
if (-not [IO.Path]::IsPathRooted($Root)) { $Root = [IO.Path]::GetFullPath((Join-Path (Get-Location).Path $Root)) }

$ErrorActionPreference = 'Stop'

function Fail([string]$Message) {
    Write-Host "ERROR: $Message" -ForegroundColor Red
    exit 1
}

function Get-Sha256([string]$Path) {
    $sha = [System.Security.Cryptography.SHA256]::Create()
    try {
        $stream = [System.IO.File]::OpenRead($Path)
        try {
            $hash = $sha.ComputeHash($stream)
            return ([System.BitConverter]::ToString($hash)).Replace('-', '').ToLowerInvariant()
        }
        finally { $stream.Dispose() }
    }
    finally { $sha.Dispose() }
}

function As-Property($Object, [string]$Name) {
    if ($null -eq $Object) { return $null }
    $prop = $Object.PSObject.Properties[$Name]
    if ($null -eq $prop) { return $null }
    return $prop.Value
}

$rootFull = [System.IO.Path]::GetFullPath($Root)
$sourceFull = [System.IO.Path]::GetFullPath((Join-Path $rootFull $SourceDir))
$outputFull = [System.IO.Path]::GetFullPath((Join-Path $rootFull $Output))

if (-not (Test-Path -LiteralPath $sourceFull -PathType Container)) {
    Fail "找不到条目目录：$sourceFull"
}

$downloadsFull = Join-Path $rootFull 'downloads.json'
$downloadsMap = @{}
if (Test-Path -LiteralPath $downloadsFull -PathType Leaf) {
    try {
        $downloadsRaw = [System.IO.File]::ReadAllText($downloadsFull, [System.Text.Encoding]::UTF8)
        $downloadsObj = $downloadsRaw | ConvertFrom-Json
        foreach ($prop in $downloadsObj.PSObject.Properties) {
            $downloadsMap[$prop.Name] = $prop.Value
        }
    }
    catch {
        Fail "downloads.json 不是有效的 JSON：$($_.Exception.Message)"
    }
}

$files = @(Get-ChildItem -LiteralPath $sourceFull -Filter '*.json' -File | Sort-Object Name)
$entries = New-Object System.Collections.ArrayList
$errors = New-Object System.Collections.ArrayList
$seen = New-Object 'System.Collections.Generic.HashSet[string]' ([System.StringComparer]::OrdinalIgnoreCase)

foreach ($file in $files) {
    $label = $file.Name
    try {
        $raw = [System.IO.File]::ReadAllText($file.FullName, [System.Text.Encoding]::UTF8)
        $obj = $raw | ConvertFrom-Json
    }
    catch {
        [void]$errors.Add("${label}：不是有效的 JSON（$($_.Exception.Message)）")
        continue
    }

    $id = [string](As-Property $obj 'id')
    if ([string]::IsNullOrWhiteSpace($id)) {
        [void]$errors.Add("${label}：缺少 id")
        continue
    }
    if ($id -cnotmatch '^[a-z0-9][a-z0-9_\-\.]{0,63}$') {
        [void]$errors.Add("${label}：id「$id」不符合 ^[a-z0-9][a-z0-9_.-]{0,63}$")
        continue
    }
    if (-not $seen.Add($id)) {
        [void]$errors.Add("${label}：id「$id」重复")
        continue
    }

    $name = [string](As-Property $obj 'name')
    if ([string]::IsNullOrWhiteSpace($name)) { [void]$errors.Add("${label}：缺少 name") }

    $latest = As-Property $obj 'latest'
    if ($null -eq $latest) {
        [void]$errors.Add("${label}：缺少 latest")
        continue
    }

    $version = [string](As-Property $latest 'version')
    if ($version -cnotmatch '^\d+(\.\d+){1,3}(-[0-9A-Za-z.\-]+)?$') {
        [void]$errors.Add("${label}：latest.version「$version」不合法")
    }

    $minApi = As-Property $latest 'minApiVersion'
    $minApiValue = 0
    if (-not [int]::TryParse([string]$minApi, [ref]$minApiValue) -or $minApiValue -lt 1) {
        [void]$errors.Add("${label}：latest.minApiVersion 必须是正整数")
    }

    $download = [string](As-Property $latest 'download')
    $downloadOk = $false
    if (-not [string]::IsNullOrWhiteSpace($download)) {
        $uri = $null
        if ([System.Uri]::TryCreate($download, [System.UriKind]::Absolute, [ref]$uri)) {
            $downloadOk = ($uri.Scheme -eq 'http' -or $uri.Scheme -eq 'https')
        }
    }
    if (-not $downloadOk) { [void]$errors.Add("${label}：latest.download 必须是 http(s) 绝对地址") }

    $sha = ([string](As-Property $latest 'sha256')).Trim().ToLowerInvariant()
    $size = [long]0
    $hasSize = [long]::TryParse([string](As-Property $latest 'size'), [ref]$size) -and $size -gt 0

    $package = [string](As-Property $latest 'package')
    if (-not [string]::IsNullOrWhiteSpace($package)) {
        $packageFull = [System.IO.Path]::GetFullPath((Join-Path $file.DirectoryName $package))
        if (-not (Test-Path -LiteralPath $packageFull -PathType Leaf)) {
            [void]$errors.Add("${label}：package 指向的文件不存在：$packageFull")
        }
        else {
            $actualSize = (Get-Item -LiteralPath $packageFull).Length
            $actualSha = Get-Sha256 $packageFull
            if ($hasSize -and $size -ne $actualSize) {
                [void]$errors.Add("${label}：size 与 package 实际大小不一致（填写 $size，实际 $actualSize）")
            }
            if ($sha -and $sha -ne $actualSha) {
                [void]$errors.Add("${label}：sha256 与 package 实际摘要不一致")
            }
            $size = $actualSize
            $sha = $actualSha
            $hasSize = $true
        }
    }

    if (-not $hasSize) { [void]$errors.Add("${label}：缺少 size（或提供 latest.package 自动计算）") }
    if ($sha -cnotmatch '^[0-9a-f]{64}$') {
        [void]$errors.Add("${label}：sha256 必须是 64 位十六进制（或提供 latest.package 自动计算）")
    }

    $publishedAt = [string](As-Property $latest 'publishedAt')
    if ([string]::IsNullOrWhiteSpace($publishedAt)) {
        $publishedAt = $file.LastWriteTimeUtc.ToString('yyyy-MM-ddTHH:mm:ssZ')
    }

    $entry = [ordered]@{
        id          = $id
        name        = $name
        summary     = [string](As-Property $obj 'summary')
        description = [string](As-Property $obj 'description')
        author      = [string](As-Property $obj 'author')
        homepage    = [string](As-Property $obj 'homepage')
        license     = [string](As-Property $obj 'license')
        tags        = @((As-Property $obj 'tags') | Where-Object { $_ } | ForEach-Object { [string]$_ })
        featured    = [bool](As-Property $obj 'featured')
        latest      = [ordered]@{
            version      = $version
            minApiVersion = $minApiValue
            publishedAt  = $publishedAt
            size         = $size
            sha256       = $sha
            download     = $download
            notes        = [string](As-Property $latest 'notes')
        }
    }
    $dl = $downloadsMap[$id]
    if ($null -ne $dl) {
        $total = [long]-1
        if ([long]::TryParse([string](As-Property $dl 'total'), [ref]$total) -and $total -ge 0) {
            $entry.downloads = [ordered]@{
                total     = $total
                updatedAt = [string](As-Property $dl 'updatedAt')
                source    = [string](As-Property $dl 'source')
            }
        }
    }
    [void]$entries.Add([pscustomobject]$entry)
}

if ($errors.Count -gt 0) {
    Write-Host "索引校验失败，共 $($errors.Count) 个问题：" -ForegroundColor Red
    foreach ($e in $errors) { Write-Host "  - $e" -ForegroundColor Red }
    exit 1
}

$sorted = @($entries | Sort-Object @{ Expression = { $_.featured }; Descending = $true }, @{ Expression = { $_.id }; Descending = $false })

$lines = New-Object System.Collections.Generic.List[string]
$lines.Add('{')
$lines.Add('  "schema": 1,')
$lines.Add('  "generatedAt": "' + (Get-Date).ToUniversalTime().ToString('yyyy-MM-ddTHH:mm:ssZ') + '",')
if ($sorted.Count -eq 0) {
    $lines.Add('  "plugins": []')
}
else {
    $lines.Add('  "plugins": [')
    for ($i = 0; $i -lt $sorted.Count; $i++) {
        $compact = $sorted[$i] | ConvertTo-Json -Depth 10 -Compress
        $indented = '    ' + $compact
        if ($i -lt $sorted.Count - 1) { $indented += ',' }
        $lines.Add($indented)
    }
    $lines.Add('  ]')
}
$lines.Add('}')
$text = ($lines -join "`r`n") + "`r`n"

if ($Check) {
    if ($sorted.Count -eq 0) {
        Write-Host 'WARN: 索引里还没有插件（允许的空索引）。' -ForegroundColor Yellow
    }
    Write-Host "OK: $($sorted.Count) 个条目通过校验。" -ForegroundColor Green
    exit 0
}

$tmp = $outputFull + '.tmp'
[System.IO.File]::WriteAllText($tmp, $text, (New-Object System.Text.UTF8Encoding($false)))
if (Test-Path -LiteralPath $outputFull) { Remove-Item -LiteralPath $outputFull -Force }
[System.IO.File]::Move($tmp, $outputFull)

if ($sorted.Count -eq 0) {
    Write-Host 'WARN: 索引里还没有插件，已生成空索引。' -ForegroundColor Yellow
}
Write-Host "OK: 已生成 $outputFull（$($sorted.Count) 个条目）" -ForegroundColor Green
