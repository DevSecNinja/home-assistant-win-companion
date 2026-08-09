#ifndef MyAppVersion
  #error MyAppVersion must be provided
#endif
#ifndef SourceDir
  #error SourceDir must be provided
#endif
#ifndef OutputDir
  #error OutputDir must be provided
#endif
#ifndef Architecture
  #error Architecture must be provided
#endif

#define MyAppName "WindowsCompanion"
#define MyAppExeName "WindowsCompanion.exe"
#define MyAppPublisher "DevSecNinja"
#define MyAppUrl "https://github.com/DevSecNinja/home-assistant-win-companion"

#if Architecture == "x64"
  #define AllowedArchitectures "x64compatible and not arm64"
  #define InstallArchitecture "x64compatible"
#elif Architecture == "arm64"
  #define AllowedArchitectures "arm64"
  #define InstallArchitecture "arm64"
#else
  #error Unsupported Architecture
#endif

[Setup]
AppId={{839A8E9A-16E3-4365-B7B1-DBF18106B119}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppUrl}
AppSupportURL={#MyAppUrl}/issues
AppUpdatesURL={#MyAppUrl}/releases
AppComments=An independent Windows companion for Home Assistant.
DefaultDirName={localappdata}\Programs\WindowsCompanion
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed={#AllowedArchitectures}
ArchitecturesInstallIn64BitMode={#InstallArchitecture}
MinVersion=10.0.17763
OutputDir={#OutputDir}
OutputBaseFilename=WindowsCompanion-{#MyAppVersion}-win-{#Architecture}-setup
SetupIconFile=..\src\HaCompanion.App\Assets\AppIcon.ico
UninstallDisplayIcon={app}\{#MyAppExeName}
LicenseFile=..\LICENSE
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern dynamic
UseSetupLdr=no
CloseApplications=yes
RestartApplications=no
AppMutex=Local\WindowsCompanion.Instance
VersionInfoVersion={#MyAppVersion}
VersionInfoCompany={#MyAppPublisher}
VersionInfoDescription={#MyAppName} installer
VersionInfoProductName={#MyAppName}
VersionInfoProductVersion={#MyAppVersion}

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: none; ValueName: "HaCompanion"; Flags: uninsdeletevalue dontcreatekey

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Launch {#MyAppName}"; Flags: nowait postinstall skipifsilent
