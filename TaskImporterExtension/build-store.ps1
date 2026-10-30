<#
.SYNOPSIS
    Builds the Microsoft Store bundle: Release MSIX for x64 and ARM64, bundled
    into one .msixbundle ready to upload to Partner Center.

.DESCRIPTION
    The packages are left unsigned on purpose. The Store signs them on
    submission, and a locally signed package is rejected because its
    certificate cannot match the Partner Center publisher.

    Needs the .NET 10 SDK and the Windows SDK 10.0.26100 (for makeappx and
    Windows.winmd). No Visual Studio.

    Bump the version in Package.appxmanifest and AppxPackageVersion in the
    csproj together before each submission; the Store refuses a version it has
    already seen.
#>
param(
    [string]$OutDir = (Join-Path $PSScriptRoot 'AppPackages')
)

$ErrorActionPreference = 'Stop'

$projectDir = Join-Path $PSScriptRoot 'TaskImporterExtension'
$project = Join-Path $projectDir 'TaskImporterExtension.csproj'

$csprojVersion = ([xml](Get-Content -Raw $project)).Project.PropertyGroup.AppxPackageVersion |
    Where-Object { $_ } | Select-Object -First 1
$manifestVersion = ([xml](Get-Content -Raw (Join-Path $projectDir 'Package.appxmanifest'))).Package.Identity.Version

if ($csprojVersion -ne $manifestVersion) {
    throw "Version mismatch: csproj AppxPackageVersion is $csprojVersion, Package.appxmanifest says $manifestVersion."
}
if ($manifestVersion -notmatch '^\d+\.\d+\.\d+\.0$') {
    throw "Version $manifestVersion must end in .0; the Store reserves the fourth part."
}
$version = $manifestVersion

$makeappx = Get-ChildItem 'C:\Program Files (x86)\Windows Kits\10\bin\*\x64\makeappx.exe' -ErrorAction SilentlyContinue |
    Sort-Object FullName -Descending | Select-Object -First 1
if (-not $makeappx) { throw 'makeappx.exe not found. Install the Windows SDK 10.0.26100.' }

if (Test-Path $OutDir) { Remove-Item -Recurse -Force $OutDir }

$packages = foreach ($arch in 'x64', 'ARM64') {
    Write-Host "`n=== $arch ===" -ForegroundColor Cyan
    $archDir = Join-Path $OutDir $arch
    # AppxBundle=Never: with scaled assets the tooling would otherwise split
    # them into resource packages and wrap each architecture in its own
    # bundle, and bundles cannot be nested. The one bundle is made below.
    dotnet build $project -c Release "-p:Platform=$arch" `
        -p:GenerateAppxPackageOnBuild=true `
        -p:AppxPackageSigningEnabled=false `
        -p:AppxBundle=Never `
        "-p:AppxPackageDir=$archDir\" | Out-Host  # keep build output out of $packages
    if ($LASTEXITCODE -ne 0) { throw "Build failed for $arch." }

    $msix = Get-ChildItem $archDir -Recurse -Filter "*_${version}_*.msix" | Select-Object -First 1
    if (-not $msix) { throw "No .msix produced for $arch." }
    $msix
}

$mapping = Join-Path $OutDir 'bundle_mapping.txt'
$lines = @('[Files]') + ($packages | ForEach-Object { "`"$($_.FullName)`" `"$($_.Name)`"" })
Set-Content -Path $mapping -Value $lines -Encoding utf8

$bundle = Join-Path $OutDir "SuperPalette_$version.msixbundle"
& $makeappx.FullName bundle /o /f $mapping /p $bundle
if ($LASTEXITCODE -ne 0) { throw 'makeappx bundle failed.' }

Write-Host "`nUpload this to Partner Center:" -ForegroundColor Green
Write-Host "  $bundle"
