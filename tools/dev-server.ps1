#requires -Version 5.1
<#
.SYNOPSIS
  本地静态服务器，用于预览插件市场索引和安装包。

.DESCRIPTION
  只监听回环地址，默认把仓库下 `_dev\www` 作为站点根目录，端口 8765。
  把生成好的 index.json、packages\*.pclplugin 放进该目录，然后在
  「插件设置 → 索引地址」里填 http://127.0.0.1:8765/index.json 即可调试。

  首次访问 127.0.0.1 时，市场插件会通过 net.dynamic 权限弹出授权框，
  这是 PCL Harness 的预期行为。

.EXAMPLE
  powershell -ExecutionPolicy Bypass -File tools\dev-server.ps1
  powershell -ExecutionPolicy Bypass -File tools\dev-server.ps1 -Port 9000
#>
[CmdletBinding()]
param(
    [int]$Port = 8765,
    [string]$Root = '',
    [string]$Bind = '127.0.0.1'
)

$ErrorActionPreference = 'Stop'

if ([string]::IsNullOrWhiteSpace($Root)) {
    $Root = Join-Path (Split-Path $PSScriptRoot -Parent) '_dev\www'
}
$rootFull = [System.IO.Path]::GetFullPath($Root)
if (-not (Test-Path -LiteralPath $rootFull -PathType Container)) {
    Write-Host "站点目录不存在：$rootFull" -ForegroundColor Red
    Write-Host '请先创建该目录，放入 index.json 与 packages\*.pclplugin。' -ForegroundColor Yellow
    exit 1
}
$prefix = $rootFull.TrimEnd([System.IO.Path]::DirectorySeparatorChar) + [System.IO.Path]::DirectorySeparatorChar

function Get-ContentType([string]$Path) {
    switch ([System.IO.Path]::GetExtension($Path).ToLowerInvariant()) {
        '.json' { return 'application/json; charset=utf-8' }
        '.html' { return 'text/html; charset=utf-8' }
        '.txt' { return 'text/plain; charset=utf-8' }
        '.md' { return 'text/plain; charset=utf-8' }
        '.png' { return 'image/png' }
        '.jpg' { return 'image/jpeg' }
        '.svg' { return 'image/svg+xml' }
        '.pclplugin' { return 'application/octet-stream' }
        '.zip' { return 'application/zip' }
        default { return 'application/octet-stream' }
    }
}

function Send-Response($Stream, [int]$Status, [string]$Reason, [byte[]]$Body, [string]$ContentType) {
    if ($null -eq $Body) { $Body = [byte[]]@() }
    $head = "HTTP/1.1 $Status $Reason`r`n" +
            "Content-Type: $ContentType`r`n" +
            "Content-Length: $($Body.Length)`r`n" +
            "Cache-Control: no-store`r`n" +
            "Connection: close`r`n`r`n"
    $headBytes = [System.Text.Encoding]::ASCII.GetBytes($head)
    $Stream.Write($headBytes, 0, $headBytes.Length)
    if ($Body.Length -gt 0) { $Stream.Write($Body, 0, $Body.Length) }
    $Stream.Flush()
}

try {
    $listener = New-Object System.Net.Sockets.TcpListener ([System.Net.IPAddress]::Parse($Bind)), $Port
    $listener.Start()
}
catch {
    Write-Host "无法监听 ${Bind}:${Port}：$($_.Exception.Message)" -ForegroundColor Red
    exit 1
}

Write-Host "Serving $rootFull"
Write-Host "http://${Bind}:${Port}/index.json" -ForegroundColor Cyan
Write-Host '按 Ctrl+C 停止。' -ForegroundColor DarkGray

try {
    while ($true) {
        $client = $listener.AcceptTcpClient()
        try {
            $client.ReceiveTimeout = 5000
            $stream = $client.GetStream()
            $reader = New-Object System.IO.StreamReader ($stream, [System.Text.Encoding]::ASCII, $false, 1024, $true)
            $requestLine = $reader.ReadLine()
            if ([string]::IsNullOrWhiteSpace($requestLine)) { continue }

            while ($true) {
                $header = $reader.ReadLine()
                if ($null -eq $header -or $header -eq '') { break }
            }

            $parts = $requestLine.Split(' ')
            if ($parts.Count -lt 2) { continue }
            $method = $parts[0].ToUpperInvariant()
            $target = $parts[1]
            $pathOnly = ($target -split '\?')[0]
            $relative = [System.Uri]::UnescapeDataString($pathOnly).TrimStart('/')
            if ([string]::IsNullOrWhiteSpace($relative)) { $relative = 'index.json' }
            if ($relative.EndsWith('/')) { $relative += 'index.json' }

            $candidate = [System.IO.Path]::GetFullPath((Join-Path $rootFull $relative))
            if (-not $candidate.StartsWith($prefix, [System.StringComparison]::OrdinalIgnoreCase)) {
                Send-Response $stream 403 'Forbidden' ([System.Text.Encoding]::UTF8.GetBytes('403 Forbidden')) 'text/plain; charset=utf-8'
                Write-Host "403 $relative" -ForegroundColor DarkYellow
                continue
            }

            if (-not (Test-Path -LiteralPath $candidate -PathType Leaf)) {
                Send-Response $stream 404 'Not Found' ([System.Text.Encoding]::UTF8.GetBytes('404 Not Found')) 'text/plain; charset=utf-8'
                Write-Host "404 $relative" -ForegroundColor DarkYellow
                continue
            }

            $bytes = [System.IO.File]::ReadAllBytes($candidate)
            $type = Get-ContentType $candidate
            if ($method -eq 'HEAD') {
                Send-Response $stream 200 'OK' ([byte[]]@()) $type
            }
            else {
                Send-Response $stream 200 'OK' $bytes $type
            }
            Write-Host "200 $relative ($($bytes.Length) bytes)" -ForegroundColor DarkGray
        }
        catch {
            Write-Host "请求处理失败：$($_.Exception.Message)" -ForegroundColor DarkYellow
        }
        finally {
            $client.Close()
        }
    }
}
finally {
    $listener.Stop()
}
