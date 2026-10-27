#!/usr/bin/env pwsh
<#
 Нативная (NativeAOT) сборка rasm и rasim.

   .\build.ps1                    # RIDы текущей платформы (Windows: win-x64, win-arm64)
   .\build.ps1 -Rid win-x64       # только нужные RID
   .\build.ps1 -Tool rasim         # только нужные утилиты (rasm, rasim)
   .\build.ps1 -Patch 42          # переопределить Patch (по умолчанию = число коммитов минус PatchBase)

 Результат: artifacts/<rid>/<tool>[.exe] и единый архив artifacts/risca-sdk-<version>-<rid>.zip
 (инструменты + doc/ + samples/lib как lib/)
 Major и Minor берутся из version.props, Patch — отсюда или из CI.
#>
param(
    [string[]]$Rid,
    [string[]]$Tool = @('rasm', 'rasim'),
    [int]$Patch = -1,
    [string]$OutDir = 'artifacts',
    [switch]$NoZip
)
$ErrorActionPreference = 'Stop'

$root = $PSScriptRoot
# имя утилиты -> проект; имя бинарника совпадает с ключом
$projects = [ordered]@{
    'rasm' = 'src/RiscA.Assembler/RiscA.Assembler.csproj'
    'rasim' = 'src/RiscA.Rasim/RiscA.Rasim.csproj'
}
$unknown = @($Tool | Where-Object { $projects.Keys -notcontains $_ })
if ($unknown.Count -gt 0) { throw "unknown tool: $($unknown -join ', ') (known: $($projects.Keys -join ', '))" }
$outRoot = if ([IO.Path]::IsPathRooted($OutDir)) { $OutDir } else { Join-Path $root $OutDir }

if (-not $Rid -or $Rid.Count -eq 0) {
    $Rid = if ($IsWindows) { @('win-x64', 'win-arm64') }
           elseif ($IsMacOS) { @('osx-arm64') }
           else { @('linux-x64') }
}

$versionProps = Get-Content (Join-Path $root 'version.props') -Raw
function Get-VersionProp([string]$name, [int]$fallback) {
    $m = [regex]::Match($versionProps, "<$name>(\d+)</$name>")
    if ($m.Success) { return [int]$m.Groups[1].Value }
    return $fallback
}
$major = Get-VersionProp 'MajorVersion' 0
$minor = Get-VersionProp 'MinorVersion' 0
$base  = Get-VersionProp 'PatchBase' 0

# Patch = число коммитов в HEAD минус PatchBase (0.9.<N> после релиза 0.9.0).
if ($Patch -lt 0 -and $env:PATCH -match '^\d+$') { $Patch = [int]$env:PATCH }
if ($Patch -lt 0) {
    $count = (& git -C $root rev-list --count HEAD 2>$null)
    if ($LASTEXITCODE -eq 0 -and $count -match '^\d+$') { $Patch = [Math]::Max(0, [int]$count - $base) } else { $Patch = 0 }
}
$version = "$major.$minor.$Patch"
Write-Host "$($Tool -join ', ') $version  (major/minor: version.props, patch: $Patch)" -ForegroundColor Green

foreach ($r in $Rid) {
    $out = Join-Path $outRoot $r
    Write-Host "`n==> publish $r" -ForegroundColor Cyan
    $aot = @()
    if ($r -eq 'linux-arm64') {
        # Кросс-сборка aarch64 на x64-хосте: мультиархитектурный lld вместо bfd-линкера хоста
        # и objcopy из binutils-aarch64-linux-gnu вместо objcopy хоста.
        $aot = @('-p:LinkerFlavor=lld', '-p:ObjCopyName=aarch64-linux-gnu-objcopy')
    }
    # переменная цикла называется $name, а не $tool: имя, отличающееся от параметра $Tool
    # только регистром, для PowerShell та же переменная (в итоге в размер печаталось System.String[])
    foreach ($name in $Tool) {
        $project = Join-Path $root $projects[$name]
        dotnet publish $project -c Release -r $r "-p:MajorVersion=$major" "-p:MinorVersion=$minor" "-p:PatchVersion=$Patch" @aot --nologo -o $out
        if ($LASTEXITCODE -ne 0) { throw "dotnet publish $name -r $r failed" }

        $exe = if ($r.StartsWith('win')) { "$name.exe" } else { $name }
        Write-Host ("  {0,-6} {1,-12} {2,10:N0} KB" -f [string]$name, $r, ((Get-Item (Join-Path $out $exe)).Length / 1KB))
    }

    if (-not $NoZip) {
        # единый SDK-архив: инструменты этой публикации + доки + ассемблерные библиотеки (samples/lib -> lib/)
        $stage = Join-Path $outRoot ".sdk-stage-$r"
        Remove-Item $stage -Recurse -Force -ErrorAction SilentlyContinue
        New-Item -ItemType Directory -Path $stage | Out-Null
        foreach ($name in $Tool) {
            $exe = if ($r.StartsWith('win')) { "$name.exe" } else { $name }
            Copy-Item (Join-Path $out $exe) $stage
        }
        Copy-Item (Join-Path $root 'samples/lib') (Join-Path $stage 'lib') -Recurse
        Copy-Item (Join-Path $root 'doc') (Join-Path $stage 'doc') -Recurse
        $zip = Join-Path $outRoot "risca-sdk-$version-$r.zip"
        Compress-Archive -Path (Join-Path $stage '*') -DestinationPath $zip -Force
        Remove-Item $stage -Recurse -Force
        Write-Host "  zip: $zip"
    }
}
