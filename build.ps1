#!/usr/bin/env pwsh
<#
 Нативная (NativeAOT) сборка rasm.

   .\build.ps1                    # RIDы текущей платформы (Windows: win-x64, win-arm64)
   .\build.ps1 -Rid win-x64       # только нужные RID
   .\build.ps1 -Minor 42          # переопределить Minor (по умолчанию = число коммитов в HEAD)

 Результат: artifacts/<rid>/rasm[.exe] и artifacts/rasm-<version>-<rid>.zip
 Major берётся из version.props, Minor/Patch — сюда или из CI.
#>
param(
    [string[]]$Rid,
    [int]$Minor = -1,
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

$majorMatch = Select-String -Path (Join-Path $root 'version.props') -Pattern '<MajorVersion>(\d+)</MajorVersion>'
if (-not $majorMatch) { throw "MajorVersion not found in version.props" }
$major = [int]$majorMatch.Matches[0].Groups[1].Value

if ($Minor -lt 0 -and $env:MINOR -match '^\d+$') { $Minor = [int]$env:MINOR }
if ($Minor -lt 0) {
    $count = (& git -C $root rev-list --count HEAD 2>$null)
    if ($LASTEXITCODE -eq 0 -and $count -match '^\d+$') { $Minor = [int]$count } else { $Minor = 0 }
}
$version = "$major.$Minor.0"
Write-Host "rasm $version  (major: version.props, minor: $Minor)" -ForegroundColor Green

foreach ($r in $Rid) {
    $out = Join-Path $outRoot $r
    Write-Host "`n==> publish $r" -ForegroundColor Cyan
    $aot = @()
    if ($r -eq 'linux-arm64') {
        # Кросс-сборка aarch64 на x64-хосте: мультиархитектурный lld вместо bfd-линкера хоста
        # и objcopy из binutils-aarch64-linux-gnu вместо objcopy хоста.
        $aot = @('-p:LinkerFlavor=lld', '-p:ObjCopyName=aarch64-linux-gnu-objcopy')
    }
    dotnet publish $project -c Release -r $r "-p:MajorVersion=$major" "-p:MinorVersion=$Minor" @aot --nologo -o $out
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish -r $r failed" }

    $exe = if ($r.StartsWith('win')) { 'rasm.exe' } else { 'rasm' }
    Write-Host ("  {0,-12} {1,10:N0} KB" -f $r, ((Get-Item (Join-Path $out $exe)).Length / 1KB))

    if (-not $NoZip) {
        $zip = Join-Path $outRoot "rasm-$version-$r.zip"
        Compress-Archive -Path (Join-Path $out $exe) -DestinationPath $zip -Force
        Write-Host "  zip: $zip"
    }
}
