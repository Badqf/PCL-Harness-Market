<#
.SYNOPSIS
    把插件市场打包成 .pclplugin（zip）。

.DESCRIPTION
    只打包运行必需的文件：plugin.json、清单中的 dll、LICENSE。
    不会把 index/、tools/、docs/、samples/、.git/ 等带进包里。

.PARAMETER Project
    插件目录，默认本脚本所在目录。

.PARAMETER Output
    输出文件或目录。默认 dist\<id>-<version>.pclplugin。
#>
[CmdletBinding()]
param(
    [string]$Project = '',
    [string]$Output
)

if ([string]::IsNullOrWhiteSpace($Project)) { $Project = $PSScriptRoot }

$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem

$projectDir = (Get-Item $Project).FullName
$jsonPath = Join-Path $projectDir "plugin.json"
if (-not (Test-Path $jsonPath)) { throw "缺少 plugin.json：$jsonPath" }
$manifest = Get-Content $jsonPath -Raw -Encoding UTF8 | ConvertFrom-Json

if (-not $manifest.id) { throw "plugin.json 缺少 id" }
if ($manifest.id -notmatch '^[a-z0-9][a-z0-9_\-\.]{0,63}$') { throw "plugin.json 的 id 不合法：$($manifest.id)" }
if (-not $manifest.version -or $manifest.version -notmatch '^\d+(\.\d+){1,3}(-[0-9A-Za-z\.\-]+)?$') { throw "plugin.json 的 version 不合法：$($manifest.version)" }
if (-not $manifest.dll) { throw "plugin.json 缺少 dll" }
if (-not $manifest.entry) { throw "plugin.json 缺少 entry" }
$dllName = [string]$manifest.dll
if ($dllName -match '[\\/]' -or $dllName.Contains('..') -or ($dllName -notmatch '^[A-Za-z0-9._\-]+\.dll$')) {
    throw "plugin.json 的 dll 字段不合法（只能是文件名）：$dllName"
}

$files = New-Object System.Collections.Generic.List[string]
$files.Add($jsonPath)
$dllPath = Join-Path $projectDir $dllName
$projectFull = [System.IO.Path]::GetFullPath($projectDir).TrimEnd('\') + '\'
if (-not [System.IO.Path]::GetFullPath($dllPath).StartsWith($projectFull, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "dll 路径越界：$dllPath"
}
if (-not (Test-Path $dllPath)) { throw "找不到 dll：$dllPath（先运行 build.ps1）" }
$files.Add($dllPath)
$licensePath = Join-Path $projectDir "LICENSE"
if (Test-Path $licensePath) { $files.Add($licensePath) }

if (-not $Output) { $Output = Join-Path (Join-Path $projectDir "dist") ("{0}-{1}.pclplugin" -f $manifest.id, $manifest.version) }
if ((Get-Item $Output -ErrorAction SilentlyContinue).PSIsContainer) {
    $Output = Join-Path $Output ("{0}-{1}.pclplugin" -f $manifest.id, $manifest.version)
}
$outDir = Split-Path $Output -Parent
if ($outDir -and -not (Test-Path $outDir)) { New-Item -ItemType Directory -Force $outDir | Out-Null }
if (Test-Path $Output) { Remove-Item $Output -Force }

$zip = [System.IO.Compression.ZipFile]::Open($Output, [System.IO.Compression.ZipArchiveMode]::Create)
try {
    foreach ($f in $files) {
        $name = Split-Path $f -Leaf
        $entry = $zip.CreateEntry($name, [System.IO.Compression.CompressionLevel]::Optimal)
        $entry.LastWriteTime = (Get-Date)
        $stream = $entry.Open()
        try {
            $bytes = [System.IO.File]::ReadAllBytes($f)
            $stream.Write($bytes, 0, $bytes.Length)
        } finally {
            $stream.Dispose()
        }
    }
} finally {
    $zip.Dispose()
}

$size = (Get-Item $Output).Length
$sha = (Get-FileHash $Output -Algorithm SHA256).Hash.ToLowerInvariant()
Write-Host ""
Write-Host "OK: $Output ($size bytes)" -ForegroundColor Green
Write-Host "sha256: $sha"
Write-Host ""
Write-Host "索引条目里可写："
Write-Host ("  version    : {0}" -f ($manifest.version))
Write-Host ("  size       : {0}" -f $size)
Write-Host ("  sha256     : {0}" -f $sha)
