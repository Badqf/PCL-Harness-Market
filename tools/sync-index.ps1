[CmdletBinding()]
param(
    [string]$Root,
    [string]$Token = $env:GITHUB_TOKEN,
    [switch]$DryRun
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version 2.0

if (-not $Root) { $Root = Split-Path -Parent $PSScriptRoot }
$Root = (Resolve-Path -LiteralPath $Root).Path
$PluginsDir = Join-Path (Join-Path $Root 'index') 'plugins'
if (-not (Test-Path -LiteralPath $PluginsDir)) {
    throw "Plugin source directory not found: $PluginsDir"
}

try {
    [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
} catch {
    # PowerShell 7 on Linux ignores this; keep going.
}

$Headers = @{
    'User-Agent'          = 'pcl-harness-market-sync'
    'Accept'              = 'application/vnd.github+json'
    'X-GitHub-Api-Version' = '2022-11-28'
}
if (-not [string]::IsNullOrWhiteSpace($Token)) {
    $Headers['Authorization'] = "Bearer $Token"
}

function ConvertTo-VersionValue {
    param([string]$Value)
    if ([string]::IsNullOrWhiteSpace($Value)) { return $null }
    $text = $Value.Trim()
    if ($text.StartsWith('v') -or $text.StartsWith('V')) { $text = $text.Substring(1) }
    $core = ($text -split '-', 2)[0]
    try { return [version]$core } catch { return $null }
}

function Test-VersionNewer {
    param([string]$Candidate, [string]$Current)
    $left = ConvertTo-VersionValue $Candidate
    $right = ConvertTo-VersionValue $Current
    if ($null -ne $left -and $null -ne $right) {
        if ($left -ne $right) { return $left -gt $right }
        $leftPre = $Candidate.Trim().TrimStart('v', 'V').Contains('-')
        $rightPre = $Current.Trim().TrimStart('v', 'V').Contains('-')
        if ($leftPre -ne $rightPre) { return (-not $leftPre) }
        return [string]::CompareOrdinal($Candidate.TrimStart('v', 'V'), $Current.TrimStart('v', 'V')) -gt 0
    }
    return [string]::CompareOrdinal($Candidate, $Current) -gt 0
}

function Get-LatestAsset {
    param($Release, [string]$Pattern)
    if ([string]::IsNullOrWhiteSpace($Pattern)) { $Pattern = '*.pclplugin' }
    $matches = @($Release.assets | Where-Object { $_.name -like $Pattern })
    if ($matches.Count -eq 0) {
        throw "No asset matching '$Pattern' in release $($Release.tag_name)"
    }
    if ($matches.Count -gt 1) {
        Write-Warning "Multiple assets match '$Pattern'; using $($matches[0].name)"
    }
    return $matches[0]
}

$updated = 0
foreach ($file in (Get-ChildItem -LiteralPath $PluginsDir -Filter '*.json' | Sort-Object Name)) {
    $entry = [IO.File]::ReadAllText($file.FullName, [Text.Encoding]::UTF8) | ConvertFrom-Json
    $id = [string]$entry.id

    if ($entry.PSObject.Properties.Name -contains 'sync' -and $entry.sync -eq $false) {
        Write-Host "[skip] $id (sync=false)"
        continue
    }
    if (-not ($entry.PSObject.Properties.Name -contains 'source')) {
        Write-Host "[skip] $id (no source)"
        continue
    }

    $source = $entry.source
    if ([string]$source.type -ne 'github') {
        Write-Host "[skip] $id (source type '$($source.type)')"
        continue
    }
    $repo = [string]$source.repo
    if ($repo -notmatch '^[^/]+/[^/]+$') {
        throw "Invalid GitHub repo for $id`: $repo"
    }

    $uri = "https://api.github.com/repos/$repo/releases/latest"
    try {
        $release = Invoke-RestMethod -Uri $uri -Headers $Headers -Method Get
    } catch {
        throw "GitHub API failed for $repo`: $($_.Exception.Message)"
    }

    $version = [string]$release.tag_name
    if ($version.StartsWith('v') -or $version.StartsWith('V')) { $version = $version.Substring(1) }
    if ([string]::IsNullOrWhiteSpace($version)) {
        throw "Release tag is empty for $repo"
    }

    $currentVersion = [string]$entry.latest.version
    if (-not (Test-VersionNewer -Candidate $version -Current $currentVersion)) {
        Write-Host "[ok] $id $currentVersion (latest $version)"
        continue
    }

    $asset = Get-LatestAsset -Release $release -Pattern ([string]$source.asset)
    $temp = Join-Path ([IO.Path]::GetTempPath()) ("pcl-market-sync-" + [guid]::NewGuid().ToString('N') + ".pclplugin")
    try {
        Invoke-WebRequest -Uri $asset.browser_download_url -OutFile $temp -UseBasicParsing -Headers @{
            'User-Agent' = 'pcl-harness-market-sync'
        } | Out-Null
        $size = (Get-Item -LiteralPath $temp).Length
        $sha = (Get-FileHash -LiteralPath $temp -Algorithm SHA256).Hash.ToLowerInvariant()
    } finally {
        Remove-Item -LiteralPath $temp -Force -ErrorAction SilentlyContinue
    }

    if ($size -le 0) { throw "Downloaded empty asset for $id" }

    $published = $release.published_at
    if ($published -is [datetime]) {
        $published = $published.ToUniversalTime().ToString('yyyy-MM-ddTHH:mm:ssZ')
    } else {
        $published = [string]$published
    }

    $entry.latest.version = $version
    $entry.latest.publishedAt = $published
    $entry.latest.size = [long]$size
    $entry.latest.sha256 = $sha
    $entry.latest.download = [string]$asset.browser_download_url

    if ($DryRun) {
        Write-Host "[dry-run] $id $currentVersion -> $version ($size bytes, $sha)"
    } else {
        $json = $entry | ConvertTo-Json -Depth 20
        if (-not $json.EndsWith("`n")) { $json += "`n" }
        $utf8 = New-Object System.Text.UTF8Encoding($false)
        [IO.File]::WriteAllText($file.FullName, $json, $utf8)
        Write-Host "[update] $id $currentVersion -> $version ($size bytes)"
    }
    $updated++
}

Write-Host "Synced $updated plugin(s)."
