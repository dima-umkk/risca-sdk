#!/usr/bin/env pwsh
<#
 Нативная (NativeAOT) сборка rasm.

   .\build.ps1                    # RIDы текущей платформы (Windows: win-x64, win-arm64)
   .\build.ps1 -Rid win-x64       # только нужные RID
   .\build.ps1 -Patch 42          # переопределить Patch (по умолчанию = число коммитов минус PatchBase)

 Результат: artifacts/<rid>/rasm[.exe] и artifacts/rasm-<version>-<rid>.zip
 Major и Minor берутся из version.props, Patch — отсюда или из CI.
#>
param(
    [string[]]$Rid,
    [int]$Patch = -1,
    [string]$OutDir = 'artifacts',
    [switch]$NoZip
)
$ErrorActionPreference = 'Stop'

$root = $PSScriptRoot
$project = Join-Path $root 'src/RiscA.Assembler/RiscA.Assembler.csproj'
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
Write-Host "rasm $version  (major/minor: version.props, patch: $Patch)" -ForegroundColor Green

foreach ($r in $Rid) {
    $out = Join-Path $outRoot $r
    Write-Host "`n==> publish $r" -ForegroundColor Cyan
    $aot = @()
    if ($r -eq 'linux-arm64') {
        # Кросс-сборка aarch64 на x64-хосте: мультиархитектурный lld вместо bfd-линкера хоста
        # и objcopy из binutils-aarch64-linux-gnu вместо objcopy хоста.
        $aot = @('-p:LinkerFlavor=lld', '-p:ObjCopyName=aarch64-linux-gnu-objcopy')
    }
    dotnet publish $project -c Release -r $r "-p:MajorVersion=$major" "-p:MinorVersion=$minor" "-p:PatchVersion=$Patch" @aot --nologo -o $out
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish -r $r failed" }

    $exe = if ($r.StartsWith('win')) { 'rasm.exe' } else { 'rasm' }
    Write-Host ("  {0,-12} {1,10:N0} KB" -f $r, ((Get-Item (Join-Path $out $exe)).Length / 1KB))

    if (-not $NoZip) {
        $zip = Join-Path $outRoot "rasm-$version-$r.zip"
        Compress-Archive -Path (Join-Path $out $exe) -DestinationPath $zip -Force
        Write-Host "  zip: $zip"
    }
}
