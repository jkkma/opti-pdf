# opti-pdf

Spanish-language Windows app (WinForms, .NET Framework 4.8) that converts
images into a single size-optimized PDF. Every output page is exactly
216 x 330 mm (Oficio / Folio), and the PDF targets a 5 MB cap.

## Download

Grab the latest [release](https://github.com/jkkma/opti-pdf/releases):

| File | What it is |
| --- | --- |
| `opti-pdf-<version>-setup.exe` | Installer. Per-user by default (no UAC prompt); can install for all users. Adds Start Menu and optional desktop shortcuts, and uninstalls from Apps & features. |
| `opti-pdf-<version>-portable.zip` | The same app as a folder. Unzip it anywhere and run `opti-pdf.exe`. Writes nothing to the registry; delete the folder to remove it. |
| `SHA256SUMS.txt` | Checksums for both. |

### Supported Windows versions

**Windows 7 SP1, 8.1, 10 and 11**, on x86, x64 and ARM64 — all from the same
download. The app is AnyCPU and ships the matching ImageMagick native library
for each architecture.

Both artifacts need **Microsoft .NET Framework 4.8**, which Windows 10 1903+
and Windows 11 already include. On Windows 7 SP1 it has to be installed, and
that itself requires the SHA-2 code-signing update (KB4474419) — so run
Windows Update to completion first. The installer checks for .NET before
copying anything and points at the download; the portable zip can't, so it
says so in its `LEEME.txt`.

Windows 7 support is enforced, not just claimed: `build-release.ps1` reads the
PE `MajorSubsystemVersion` of every shipped binary and of the compiled
installer, and fails the build if anything requires newer than Windows 7 SP1
(6.1). The arm64 native library is exempt — ARM64 Windows is 10 and newer by
definition.

## Build

- **Windows:** open `opti-pdf.slnx` in Visual Studio, or
  `msbuild opti-pdf.csproj /p:Configuration=Release` after
  `nuget restore opti-pdf.csproj -PackagesDirectory packages`.
- **Linux / cloud containers:** `bash scripts/linux-build.sh` — compile check
  only, the WinForms exe cannot run on Linux.

## Packaging

`scripts/build-release.ps1` produces both artifacts into `dist/`:

```powershell
.\scripts\build-release.ps1
```

It restores, builds Release, stages `bin\Release` minus `*.pdb` and `*.xml`,
zips that folder, compiles `installer/opti-pdf.iss`, and writes
`SHA256SUMS.txt`. Needs MSBuild and [Inno Setup 6](https://jrsoftware.org/isdl.php)
(`choco install innosetup`); NuGet is downloaded if it isn't on PATH.

Useful switches: `-Version 1.1.0`, `-SkipBuild` (package the existing
`bin\Release`), `-IsccPath`, `-OutputDir`.

The `Build` workflow runs this on every push, so the packaging never rots
unnoticed.

## Cutting a release

`Properties/AssemblyInfo.cs` is the single source of truth for the version —
`build-release.ps1` refuses to build a version that disagrees with it, so the
shipped exe and the installer can never claim different versions.

1. Bump `AssemblyVersion` and `AssemblyFileVersion` in
   `Properties/AssemblyInfo.cs`, then commit.
2. `git tag v1.1.0 && git push origin v1.1.0`.

The `Release` workflow builds both artifacts and attaches them to a new
GitHub Release with generated notes. A tag with a suffix (`v1.1.0-rc1`) is
published as a pre-release; only its numeric part has to match AssemblyInfo.

Neither artifact is code-signed, so Windows SmartScreen will warn on first
run until a release builds up reputation.
