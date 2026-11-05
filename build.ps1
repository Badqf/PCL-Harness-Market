<#
.SYNOPSIS
    编译插件市场（PCL Harness 插件）。

.DESCRIPTION
    用 Roslyn vbc 把仓库根目录的 .vb 源码编译成 .NET Framework 4.8 类库。
    在 SDK 的 build.ps1 基础上增加了 System.Web.Extensions 引用（JSON 序列化用）。

.PARAMETER Project
    插件目录（含 plugin.json），默认本脚本所在目录。

.PARAMETER Launcher
    PCL Harness.exe 路径。依次取 -Launcher、环境变量 PCL_HARNESS_EXE、
    脚本同目录、F:\PCL2-Release\PCL Harness.exe。

.PARAMETER VbcPath
    vbc.exe 路径（默认自动搜索）。

.PARAMETER ReferencePath
    .NET Framework 4.8 参考程序集目录（默认自动搜索）。

.PARAMETER Pack
    编译成功后调用 pack.ps1 生成 dist\<id>-<version>.pclplugin。
#>
[CmdletBinding()]
param(
    [string]$Project,
    [string]$Launcher,
    [string]$VbcPath,
    [string]$ReferencePath,
    [switch]$Pack
)

$ErrorActionPreference = "Stop"
if (-not $Project) { $Project = $PSScriptRoot }

function Find-Vbc {
    $candidates = @($env:PCL_HARNESS_VBC)
    foreach ($base in @("C:\Program Files\Microsoft Visual Studio", "C:\Program Files (x86)\Microsoft Visual Studio")) {
        if (Test-Path $base) {
            $candidates += Get-ChildItem $base -Recurse -Filter vbc.exe -ErrorAction SilentlyContinue |
                Where-Object { $_.FullName -like "*\Roslyn\*" } |
                Select-Object -ExpandProperty FullName
        }
    }
    # 3.8.3 安全加固：用户目录可被普通程序写入，必须用 Authenticode 确认编译器确实来自微软。
    $userVbc = Join-Path $env:USERPROFILE "roslyn\tasks\vbc.exe"
    if (Test-Path $userVbc) {
        $sig = $null
        try { $sig = Get-AuthenticodeSignature -FilePath $userVbc -ErrorAction Stop } catch { $sig = $null }
        $trusted = $false
        if ($null -ne $sig -and $sig.Status -eq 'Valid' -and $null -ne $sig.SignerCertificate) {
            $trusted = $sig.SignerCertificate.Subject -like '*Microsoft*'
        }
        if ($trusted) {
            $candidates += $userVbc
        } else {
            Write-Warning "忽略 $userVbc：它没有有效的微软 Authenticode 签名，可能是被替换过的编译器。"
        }
    }
    $candidates += @(
        "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\vbc.exe",
        "$env:WINDIR\Microsoft.NET\Framework\v4.0.30319\vbc.exe"
    )
    $found = $candidates | Where-Object { $_ -and (Test-Path $_) } | Select-Object -First 1
    if ($found -and $found -eq $userVbc) {
        Write-Warning "正在使用用户目录里的编译器（$found）——该目录可被普通程序写入，请确认来源可信；推荐安装 Visual Studio Build Tools。"
    }
    if ($found -and $found -match '\\v4\.0\.30319\\vbc\.exe$') {
        Write-Warning "找不到 Roslyn 编译器，退回 legacy vbc：它只支持 VB 10，插值字符串等新语法会编译失败。"
    }
    return $found
}

function Resolve-ReferenceDir {
    param([string]$Path)
    if (-not $Path) { return $null }
    foreach ($cand in @($Path, (Join-Path $Path ".NETFramework\v4.8"), (Join-Path $Path "v4.8"), (Join-Path $Path ".NETFramework\v4.7.2"))) {
        if ($cand -and (Test-Path (Join-Path $cand "mscorlib.dll"))) { return (Resolve-Path $cand).Path }
    }
    return $null
}

function Find-References {
    $candidates = @(
        "C:\Program Files (x86)\Reference Assemblies\Microsoft\Framework\.NETFramework\v4.8",
        "$env:ProgramFiles\Reference Assemblies\Microsoft\Framework\.NETFramework\v4.8",
        "$env:USERPROFILE\dotnet-fwreal"
    )
    $nuget = "$env:USERPROFILE\.nuget\packages\microsoft.netframework.referenceassemblies.net48"
    if (Test-Path $nuget) {
        $candidates += Get-ChildItem $nuget -Recurse -Filter mscorlib.dll -ErrorAction SilentlyContinue |
            Where-Object { $_.FullName -like "*v4.8*" } |
            ForEach-Object { $_.DirectoryName }
    }
    foreach ($c in $candidates) {
        $resolved = Resolve-ReferenceDir $c
        if ($resolved) { return $resolved }
    }
    return $null
}

function Find-Launcher {
    if ($Launcher) { return $Launcher }
    if ($env:PCL_HARNESS_EXE) { return $env:PCL_HARNESS_EXE }
    $local = Join-Path $PSScriptRoot "PCL Harness.exe"
    if (Test-Path $local) { return $local }
    $release = "F:\PCL2-Release\PCL Harness.exe"
    if (Test-Path $release) { return $release }
    return $null
}

if (-not (Test-Path $Project)) { throw "找不到插件目录：$Project" }
$projectDir = (Get-Item $Project).FullName
$sources = @(Get-ChildItem $projectDir -Filter *.vb | Select-Object -ExpandProperty FullName)
if (-not $sources) { throw "目录里没有 .vb 源码：$projectDir" }

$jsonPath = Join-Path $projectDir "plugin.json"
if (-not (Test-Path $jsonPath)) { throw "缺少 plugin.json：$jsonPath" }
$manifest = Get-Content $jsonPath -Raw -Encoding UTF8 | ConvertFrom-Json
$dllName = if ($manifest.dll) { [string]$manifest.dll } else { "Plugin.dll" }
# 3.8.3 安全加固：dll 只能是文件名，输出必须落在插件目录内。
if ($dllName -match '[\\/]' -or $dllName.Contains('..') -or ($dllName -notmatch '^[A-Za-z0-9._\-]+\.dll$')) {
    throw "plugin.json 的 dll 字段不合法（只能是文件名，例如 Market.dll）：$dllName"
}
$outDll = Join-Path $projectDir $dllName
$projectFull = [System.IO.Path]::GetFullPath($projectDir).TrimEnd('\') + '\'
if (-not [System.IO.Path]::GetFullPath($outDll).StartsWith($projectFull, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "输出路径越界：$outDll"
}

$launcherExe = Find-Launcher
if (-not $launcherExe -or -not (Test-Path $launcherExe)) {
    throw "找不到 PCL Harness.exe：用 -Launcher 指定，或设环境变量 PCL_HARNESS_EXE。"
}
if (-not $VbcPath) { $VbcPath = Find-Vbc }
if (-not $VbcPath) { throw "找不到 vbc.exe：装 Visual Studio Build Tools 或 .NET SDK，或用 -VbcPath 指定。" }
if ($ReferencePath) {
    $normalized = Resolve-ReferenceDir $ReferencePath
    if (-not $normalized) { throw "在 $ReferencePath 下找不到 mscorlib.dll。" }
    $ReferencePath = $normalized
}
if (-not $ReferencePath) { $ReferencePath = Find-References }
if (-not $ReferencePath) { throw "找不到 .NET Framework 4.8 参考程序集：用 -ReferencePath 指定。" }

$isLegacyVbc = $VbcPath -match '\\v4\.0\.30319\\vbc\.exe$'

$refs = @(
    "$ReferencePath\mscorlib.dll",
    "$ReferencePath\Microsoft.VisualBasic.dll",
    "$ReferencePath\System.dll",
    "$ReferencePath\System.Core.dll",
    "$ReferencePath\System.Data.dll",
    "$ReferencePath\System.Xml.dll",
    "$ReferencePath\System.Xml.Linq.dll",
    "$ReferencePath\System.Drawing.dll",
    "$ReferencePath\System.Xaml.dll",
    "$ReferencePath\WindowsBase.dll",
    "$ReferencePath\PresentationCore.dll",
    "$ReferencePath\PresentationFramework.dll",
    "$ReferencePath\System.Web.Extensions.dll"
)
foreach ($r in $refs) { if (-not (Test-Path $r)) { throw "缺少参考程序集：$r" } }

Write-Host "vbc        : $VbcPath"
Write-Host "参考程序集 : $ReferencePath"
Write-Host "启动器     : $launcherExe"
Write-Host "源码       : $($sources.Count) 个文件"
Write-Host "输出       : $outDll"

$effectiveRefs = $refs
if ($isLegacyVbc) {
    # legacy vbc 会自动引用自己目录的 mscorlib / Microsoft.VisualBasic / System，再显式 /r: 会 BC2000。
    $effectiveRefs = $refs | Where-Object {
        $_ -notin @("$ReferencePath\mscorlib.dll", "$ReferencePath\Microsoft.VisualBasic.dll", "$ReferencePath\System.dll")
    }
    $vbcArgs = @("/noconfig", "/nologo", "/target:library", "/out:$outDll", "/optionstrict-", "/optionexplicit+", "/optioninfer+", "/codepage:65001", "/imports:Microsoft.VisualBasic,System,System.Collections.Generic,System.Linq,System.Text", "/r:$launcherExe")
} else {
    $vbcArgs = @("/noconfig", "/nostdlib", "/nologo", "/target:library", "/out:$outDll", "/optionstrict-", "/optionexplicit+", "/optioninfer+", "/codepage:65001", "/imports:Microsoft.VisualBasic,System,System.Collections.Generic,System.Linq,System.Text", "/r:$launcherExe")
}
foreach ($r in $effectiveRefs) { $vbcArgs += "/r:$r" }
$vbcArgs += $sources

& $VbcPath @vbcArgs
$code = $LASTEXITCODE
if ($code -ne 0) { throw "编译失败（exit $code）" }

$size = (Get-Item $outDll).Length
Write-Host ""
Write-Host "OK: $outDll ($size bytes)" -ForegroundColor Green

if ($Pack) {
    & (Join-Path $PSScriptRoot "pack.ps1") -Project $projectDir
    exit $LASTEXITCODE
}

Write-Host "部署：把 plugin.json 与 $dllName 复制到 PCLHarness\Plugins\$($manifest.id)\，重启启动器。"
