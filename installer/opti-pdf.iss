; Inno Setup script for Opti-PDF. Needs Inno Setup 6.3 or later (6.x and 7.x
; both work, and both still target Windows 7 SP1).
;
; Normally driven by scripts\build-release.ps1, which passes AppVersion,
; VersionInfo, PayloadDir and OutputDir with /D. The defaults below exist only
; so the script still compiles straight from the Inno Setup IDE after a plain
; Release build.
;
; This file is UTF-8 *with BOM* on purpose. Without one Inno Setup 6 falls
; back to the system ANSI codepage and mangles the accents in the Spanish
; messages below; Inno Setup 7 refuses to compile it at all.

#ifndef AppVersion
  #define AppVersion "0.0.0-dev"
#endif
#ifndef VersionInfo
  #define VersionInfo "0.0.0.0"
#endif
#ifndef PayloadDir
  #define PayloadDir "..\bin\Release"
#endif
#ifndef OutputDir
  #define OutputDir "..\dist"
#endif

#define AppName "Opti-PDF"
#define AppExeName "opti-pdf.exe"
#define AppPublisher "jkkma"
#define AppUrl "https://github.com/jkkma/opti-pdf"

; .NET Framework 4.8 is release 528040 or higher (528049 on the Windows
; versions that shipped it separately, 533320 for 4.8.1).
#define DotNetRelease "528040"
#define DotNetUrl "https://dotnet.microsoft.com/download/dotnet-framework/net48"

[Setup]
; Keep AppId stable forever — it is what lets a new version upgrade an old
; install in place instead of leaving two entries in Programs and Features.
AppId={{48BCFC3B-A176-4553-9020-55D264F6DCD0}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher={#AppPublisher}
AppPublisherURL={#AppUrl}
AppSupportURL={#AppUrl}/issues
AppUpdatesURL={#AppUrl}/releases
VersionInfoVersion={#VersionInfo}
VersionInfoProductVersion={#VersionInfo}
VersionInfoProductTextVersion={#AppVersion}
VersionInfoCompany={#AppPublisher}
VersionInfoDescription=Opti-PDF - convierte imágenes en un PDF optimizado

DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
UninstallDisplayName={#AppName}
UninstallDisplayIcon={app}\{#AppExeName}
DisableProgramGroupPage=yes

; The app itself runs asInvoker, so a per-user install needs no UAC prompt at
; all. Default to that and let the user pick "for all users" in the dialog, or
; an admin script pass /ALLUSERS.
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=commandline dialog

; Windows 7 SP1, 8.1, 10 and 11. 6.1sp1 is also the floor .NET Framework 4.8
; itself imposes. build-release.ps1 re-checks this against the compiled
; installer's PE header, so a toolchain that quietly drops Windows 7 fails the
; build instead of shipping.
MinVersion=6.1sp1
#if Ver >= EncodeVer(6,3,0,0)
ArchitecturesInstallIn64BitMode=x64compatible
#else
ArchitecturesInstallIn64BitMode=x64 arm64
#endif

OutputDir={#OutputDir}
OutputBaseFilename=opti-pdf-{#AppVersion}-setup
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern

[Languages]
; The application is Spanish-only, so the installer is too.
Name: "es"; MessagesFile: "compiler:Languages\Spanish.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
; PayloadDir is staged by build-release.ps1 and already excludes the debug
; symbols and the IntelliSense XML. The Excludes below repeat that so an
; IDE build straight from bin\Release does not ship them either.
;
; All three Magick.Native-Q16-*.dll must ship: Magick.NET picks one at run
; time from the process architecture, and .NET Framework 4.8.1 runs natively
; on ARM64 Windows, so the arm64 one is not dead weight.
Source: "{#PayloadDir}\*"; DestDir: "{app}"; Excludes: "*.pdb,*.xml"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExeName}"
Name: "{group}\{cm:UninstallProgram,{#AppName}}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(AppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent

[Code]
function DotNetFrameworkInstalled: Boolean;
var
  Release: Cardinal;
  Key: String;
begin
  Key := 'SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full';
  Result := False;

  // The NDP key lives in the 64-bit view on 64-bit Windows; reading HKLM64
  // there errors out on a 32-bit system, hence the guard.
  if IsWin64 then
  begin
    if RegQueryDWordValue(HKLM64, Key, 'Release', Release) then
      Result := Release >= {#DotNetRelease};
  end;

  if not Result then
  begin
    if RegQueryDWordValue(HKLM32, Key, 'Release', Release) then
      Result := Release >= {#DotNetRelease};
  end;
end;

function InitializeSetup(): Boolean;
var
  ErrorCode: Integer;
  NetMsg: String;
begin
  Result := True;
  if DotNetFrameworkInstalled then
    Exit;

  // A literal rather than {cm:...}: CustomMessage cannot be used from
  // InitializeSetup, which runs before a language is active. The installer is
  // Spanish-only anyway, so there is nothing to translate against.
  NetMsg :=
    'Opti-PDF necesita Microsoft .NET Framework 4.8 y no se ha encontrado' + #13#10 +
    'en este equipo.' + #13#10 + #13#10 +
    'Instálalo y vuelve a ejecutar este programa de instalación.' + #13#10 + #13#10 +
    '¿Quieres abrir ahora la página de descarga de Microsoft?';

  if MsgBox(NetMsg, mbConfirmation, MB_YESNO) = IDYES then
    ShellExec('open', '{#DotNetUrl}', '', '', SW_SHOWNORMAL, ewNoWait, ErrorCode);

  Result := False;
end;
