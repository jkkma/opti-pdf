<#
.SYNOPSIS
    Builds the Windows release artifacts for opti-pdf.

.DESCRIPTION
    Restores, builds Release, stages a clean payload and produces two files
    in the output directory:

        opti-pdf-<version>-setup.exe      Inno Setup installer
        opti-pdf-<version>-portable.zip   the same folder, no install needed

    plus SHA256SUMS.txt covering both.

    Every shipped binary, and the compiled installer, is checked to still run
    on Windows 7 SP1 — the build fails rather than quietly dropping it.

    Requires MSBuild (Visual Studio or Build Tools) and Inno Setup 6.3 or
    later (https://jrsoftware.org/isdl.php). NuGet is downloaded if it is not
    already on PATH.

.PARAMETER Version
    Release version, e.g. 1.0.0 or 1.1.0-rc1. Defaults to the
    AssemblyFileVersion in Properties\AssemblyInfo.cs. When passed, its
    numeric part must match that AssemblyFileVersion so the installer and the
    shipped exe never disagree about what version this is.

.PARAMETER AllowVersionMismatch
    Downgrade that check to a warning. For one-off local builds only.

.EXAMPLE
    .\scripts\build-release.ps1

.EXAMPLE
    .\scripts\build-release.ps1 -Version 1.1.0
#>
[CmdletBinding()]
param(
    [string] $Version,
    [string] $Configuration = 'Release',
    [string] $OutputDir,
    [string] $IsccPath,
    [switch] $SkipBuild,
    [switch] $AllowVersionMismatch
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12

$RepoRoot = Split-Path -Parent $PSScriptRoot
if (-not $OutputDir) { $OutputDir = Join-Path $RepoRoot 'dist' }

function Write-Step([string] $Message) {
    Write-Host ''
    Write-Host "==> $Message" -ForegroundColor Cyan
}

# --- version -----------------------------------------------------------------

# AssemblyInfo.cs is the single source of truth for the version. A tagged
# release passes the tag in and we verify the two agree.
function Get-AssemblyFileVersion {
    $path = Join-Path $RepoRoot 'Properties\AssemblyInfo.cs'
    $text = Get-Content -LiteralPath $path -Raw
    $match = [regex]::Match($text, 'AssemblyFileVersion\s*\(\s*"([^"]+)"\s*\)')
    if (-not $match.Success) {
        throw "No AssemblyFileVersion found in $path."
    }
    return $match.Groups[1].Value
}

# 1.2 -> 1.2.0.0, 1.2.3 -> 1.2.3.0. Inno's VersionInfoVersion wants four parts.
function Expand-ToFourParts([string] $Value) {
    $parts = @($Value -split '\.')
    while ($parts.Count -lt 4) { $parts += '0' }
    return ($parts[0..3] -join '.')
}

$assemblyVersion = Get-AssemblyFileVersion

if (-not $Version) {
    # Trim the conventional trailing .0 so artifacts read 1.0.0, not 1.0.0.0.
    $Version = ($assemblyVersion -replace '\.0$', '')
}

# Strip any -prerelease / +build suffix: only the numeric core is comparable.
$versionCore = ($Version -split '[-+]')[0]
if ($versionCore -notmatch '^\d+(\.\d+){1,3}$') {
    throw "Version '$Version' is not of the form MAJOR.MINOR.PATCH[-prerelease]."
}

$versionInfo = Expand-ToFourParts $versionCore
$assemblyVersionInfo = Expand-ToFourParts $assemblyVersion

if ($versionInfo -ne $assemblyVersionInfo) {
    $message = "Version '$Version' does not match AssemblyFileVersion " +
               "'$assemblyVersion' in Properties\AssemblyInfo.cs. Update " +
               "AssemblyVersion and AssemblyFileVersion, or pass " +
               "-AllowVersionMismatch."
    if ($AllowVersionMismatch) {
        Write-Warning $message
    } else {
        throw $message
    }
}

Write-Host "opti-pdf $Version ($Configuration)" -ForegroundColor Green

# --- tool discovery ----------------------------------------------------------

# ProgramFiles(x86) does not exist on 32-bit Windows, and Join-Path throws on
# a null path, so filter before combining.
function Get-ProgramFilesRoots {
    return @($env:ProgramFiles, ${env:ProgramFiles(x86)}) |
        Where-Object { $_ } |
        Select-Object -Unique
}

function Resolve-MSBuild {
    $onPath = Get-Command 'msbuild.exe' -ErrorAction SilentlyContinue
    if ($onPath) { return $onPath.Source }

    foreach ($root in Get-ProgramFilesRoots) {
        $vswhere = Join-Path $root 'Microsoft Visual Studio\Installer\vswhere.exe'
        if (-not (Test-Path -LiteralPath $vswhere)) { continue }

        $found = & $vswhere -latest -prerelease -products * `
            -requires Microsoft.Component.MSBuild `
            -find 'MSBuild\**\Bin\MSBuild.exe' 2>$null
        $first = @($found) | Where-Object { $_ } | Select-Object -First 1
        if ($first) { return $first }
    }

    $message = 'MSBuild.exe not found. Install Visual Studio (or the Build ' +
        'Tools) with the .NET desktop workload, or run this from a Developer ' +
        'PowerShell prompt.'
    throw $message
}

function Resolve-NuGet {
    $onPath = Get-Command 'nuget.exe' -ErrorAction SilentlyContinue
    if ($onPath) { return $onPath.Source }

    $toolsDir = Join-Path $OutputDir 'tools'
    $local = Join-Path $toolsDir 'nuget.exe'
    if (Test-Path -LiteralPath $local) { return $local }

    Write-Host 'nuget.exe not on PATH; downloading it...'
    New-Item -ItemType Directory -Force -Path $toolsDir | Out-Null
    Invoke-WebRequest -Uri 'https://dist.nuget.org/win-x86-commandline/latest/nuget.exe' `
        -OutFile $local -UseBasicParsing
    return $local
}

function Resolve-Iscc {
    $candidates = @($IsccPath, $env:ISCC)

    # Match "Inno Setup 6", "Inno Setup 7", ... newest first, so a toolchain
    # upgrade does not silently stop finding the compiler. Any version works:
    # what actually matters for the supported Windows versions is checked
    # against the compiled installer by Assert-SupportsWindows7 below.
    foreach ($root in Get-ProgramFilesRoots) {
        Get-ChildItem -LiteralPath $root -Directory -Filter 'Inno Setup *' -ErrorAction SilentlyContinue |
            Sort-Object Name -Descending |
            ForEach-Object { $candidates += (Join-Path $_.FullName 'ISCC.exe') }
    }

    foreach ($candidate in $candidates) {
        if ($candidate -and (Test-Path -LiteralPath $candidate)) { return $candidate }
    }

    $onPath = Get-Command 'ISCC.exe' -ErrorAction SilentlyContinue
    if ($onPath) { return $onPath.Source }

    $message = 'ISCC.exe (Inno Setup 6 or later) not found. Install it from ' +
        'https://jrsoftware.org/isdl.php, or pass -IsccPath.'
    throw $message
}

# Windows refuses to start a PE whose MajorSubsystemVersion is newer than
# itself, so this is the field that decides whether the release still runs on
# Windows 7 SP1 (6.1). Checked rather than assumed: the Inno Setup version is
# whatever the build machine has installed, and a future major dropping
# Windows 7 would otherwise only surface as a user report.
function Get-PEInfo {
    param([Parameter(Mandatory = $true)][string] $Path)

    $stream = [System.IO.File]::Open($Path, 'Open', 'Read', 'ReadWrite')
    try {
        $reader = New-Object System.IO.BinaryReader($stream)
        $stream.Position = 0x3C
        $peHeader = $reader.ReadInt32()

        $stream.Position = $peHeader
        if ($reader.ReadUInt32() -ne 0x00004550) {   # 'PE\0\0'
            throw "$Path is not a PE image."
        }
        $machine = $reader.ReadUInt16()

        # PE signature (4) + COFF header (20) + offset 48 into the optional
        # header. Everything from offset 32 on is laid out the same in PE32
        # and PE32+, so this reads the same for x86 and x64.
        $stream.Position = $peHeader + 4 + 20 + 48
        $major = $reader.ReadUInt16()
        $minor = $reader.ReadUInt16()

        $architecture = switch ($machine) {
            0x014C  { 'x86' }
            0x8664  { 'x64' }
            0xAA64  { 'arm64' }
            default { '0x{0:X4}' -f $machine }
        }

        return [PSCustomObject]@{
            Name           = Split-Path -Leaf $Path
            Architecture   = $architecture
            MinimumWindows = [Version]::new($major, $minor)
        }
    } finally {
        $stream.Dispose()
    }
}

$MinimumSupportedWindows = [Version]::new(6, 1)   # Windows 7 SP1

function Assert-SupportsWindows7 {
    param([Parameter(Mandatory = $true)][string[]] $Paths)

    foreach ($path in $Paths) {
        $info = Get-PEInfo -Path $path

        # ARM64 Windows is 10 and newer by definition, so the arm64 native
        # library's own floor says nothing about ours.
        if ($info.Architecture -eq 'arm64') {
            Write-Host ('  {0,-42} {1,-5} Windows {2}+ (ARM64 Windows is 10+)' -f `
                $info.Name, $info.Architecture, $info.MinimumWindows)
            continue
        }

        if ($info.MinimumWindows -gt $MinimumSupportedWindows) {
            throw "$($info.Name) requires Windows $($info.MinimumWindows), but " +
                  "opti-pdf must run on Windows 7 SP1 ($MinimumSupportedWindows) and newer."
        }

        Write-Host ('  {0,-42} {1,-5} Windows {2}+' -f `
            $info.Name, $info.Architecture, $info.MinimumWindows)
    }
}

# --- build -------------------------------------------------------------------

$buildDir = Join-Path $RepoRoot "bin\$Configuration"

if ($SkipBuild) {
    Write-Step "Skipping build; packaging whatever is in bin\$Configuration"
    if (-not (Test-Path -LiteralPath $buildDir)) {
        throw "-SkipBuild was passed but $buildDir does not exist."
    }
} else {
    $msbuild = Resolve-MSBuild
    $nuget = Resolve-NuGet
    $csproj = Join-Path $RepoRoot 'opti-pdf.csproj'

    Write-Step 'Restoring NuGet packages'
    & $nuget restore $csproj -PackagesDirectory (Join-Path $RepoRoot 'packages') -NonInteractive
    if ($LASTEXITCODE -ne 0) { throw "nuget restore failed ($LASTEXITCODE)." }

    # Wipe the output first: the payload is built by globbing bin\<config>, so
    # a stale DLL left there from an older package version would ship.
    if (Test-Path -LiteralPath $buildDir) {
        Remove-Item -LiteralPath $buildDir -Recurse -Force
    }

    Write-Step "Building $Configuration"
    & $msbuild $csproj /t:Rebuild "/p:Configuration=$Configuration" /p:Platform=AnyCPU /v:minimal /nologo
    if ($LASTEXITCODE -ne 0) { throw "msbuild failed ($LASTEXITCODE)." }
}

# --- staging -----------------------------------------------------------------

$payloadName = "opti-pdf-$Version"
$stageRoot = Join-Path $OutputDir 'staging'
$payloadDir = Join-Path $stageRoot $payloadName

Write-Step "Staging payload in $payloadDir"

if (Test-Path -LiteralPath $stageRoot) {
    Remove-Item -LiteralPath $stageRoot -Recurse -Force
}
New-Item -ItemType Directory -Force -Path $payloadDir | Out-Null

# Everything the app needs at run time, minus the debug symbols and the
# IntelliSense XML (~2.8 MB of documentation nobody unzips).
$excludedExtensions = @('.pdb', '.xml')
$sourceRoot = (Resolve-Path -LiteralPath $buildDir).ProviderPath

Get-ChildItem -LiteralPath $sourceRoot -Recurse -File |
    Where-Object { $excludedExtensions -notcontains $_.Extension.ToLowerInvariant() } |
    ForEach-Object {
        $relative = $_.FullName.Substring($sourceRoot.Length).TrimStart('\')
        $target = Join-Path $payloadDir $relative
        $targetDir = Split-Path -Parent $target
        if (-not (Test-Path -LiteralPath $targetDir)) {
            New-Item -ItemType Directory -Force -Path $targetDir | Out-Null
        }
        Copy-Item -LiteralPath $_.FullName -Destination $target -Force
    }

Copy-Item -LiteralPath (Join-Path $RepoRoot 'installer\LEEME.txt') `
    -Destination (Join-Path $payloadDir 'LEEME.txt') -Force

# A missing native DLL only surfaces as a crash on the user's machine, so
# check here rather than trusting the glob.
$required = @(
    'opti-pdf.exe'
    'opti-pdf.exe.config'
    'Magick.NET-Q16-AnyCPU.dll'
    'Magick.NET.Core.dll'
    'Magick.Native-Q16-x64.dll'   # 64-bit Windows
    'Magick.Native-Q16-x86.dll'   # 32-bit Windows, and ARM64 before Win11
    'Magick.Native-Q16-arm64.dll' # ARM64 with .NET Framework 4.8.1
    'LEEME.txt'
)
$missing = @($required | Where-Object { -not (Test-Path -LiteralPath (Join-Path $payloadDir $_)) })
if ($missing.Count -gt 0) {
    throw "Payload is incomplete, missing: $($missing -join ', ')"
}

$payloadFiles = @(Get-ChildItem -LiteralPath $payloadDir -Recurse -File)
$payloadSize = ($payloadFiles | Measure-Object -Property Length -Sum).Sum
Write-Host ("Payload: {0} files, {1:N1} MB" -f $payloadFiles.Count, ($payloadSize / 1MB))

Write-Step 'Checking the payload still runs on Windows 7 SP1'
Assert-SupportsWindows7 -Paths @(
    $payloadFiles |
        Where-Object { $_.Extension -in '.exe', '.dll' } |
        Sort-Object Name |
        ForEach-Object { $_.FullName }
)

# --- portable zip ------------------------------------------------------------

$zipPath = Join-Path $OutputDir "opti-pdf-$Version-portable.zip"

Write-Step "Creating $(Split-Path -Leaf $zipPath)"

if (Test-Path -LiteralPath $zipPath) { Remove-Item -LiteralPath $zipPath -Force }
# Windows PowerShell needs the assembly loaded; PowerShell 7 already has the
# type and has no assembly by that name to load.
if (-not ('System.IO.Compression.ZipFile' -as [type])) {
    Add-Type -AssemblyName System.IO.Compression.FileSystem
}
# includeBaseDirectory: the archive holds one opti-pdf-<version>\ folder
# instead of spraying 28 files wherever the user extracts it.
[System.IO.Compression.ZipFile]::CreateFromDirectory(
    $payloadDir, $zipPath, [System.IO.Compression.CompressionLevel]::Optimal, $true)

# --- installer ---------------------------------------------------------------

$iscc = Resolve-Iscc
$iss = Join-Path $RepoRoot 'installer\opti-pdf.iss'

Write-Step "Compiling installer with $iscc"

& $iscc `
    "/DAppVersion=$Version" `
    "/DVersionInfo=$versionInfo" `
    "/DPayloadDir=$payloadDir" `
    "/DOutputDir=$OutputDir" `
    $iss
if ($LASTEXITCODE -ne 0) { throw "ISCC failed ($LASTEXITCODE)." }

$setupPath = Join-Path $OutputDir "opti-pdf-$Version-setup.exe"
if (-not (Test-Path -LiteralPath $setupPath)) {
    throw "ISCC reported success but $setupPath is missing."
}

Assert-SupportsWindows7 -Paths @($setupPath)

# --- checksums ---------------------------------------------------------------

Write-Step 'Writing SHA256SUMS.txt'

$artifacts = @($setupPath, $zipPath)
$lines = foreach ($artifact in $artifacts) {
    $hash = (Get-FileHash -LiteralPath $artifact -Algorithm SHA256).Hash.ToLowerInvariant()
    '{0}  {1}' -f $hash, (Split-Path -Leaf $artifact)
}
$sumsPath = Join-Path $OutputDir 'SHA256SUMS.txt'
Set-Content -LiteralPath $sumsPath -Value $lines -Encoding ASCII

# --- summary -----------------------------------------------------------------

Write-Host ''
Write-Host 'Done.' -ForegroundColor Green
foreach ($artifact in $artifacts) {
    $size = (Get-Item -LiteralPath $artifact).Length / 1MB
    Write-Host ('  {0,-40} {1,6:N1} MB' -f (Split-Path -Leaf $artifact), $size)
}
Write-Host ('  {0,-40}' -f (Split-Path -Leaf $sumsPath))
