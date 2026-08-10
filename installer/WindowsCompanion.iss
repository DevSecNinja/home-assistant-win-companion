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
SetupIconFile=..\src\WindowsCompanion.App\Assets\AppIcon.ico
UninstallDisplayIcon={app}\{#MyAppExeName}
LicenseFile=..\LICENSE
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern dynamic
UseSetupLdr=no
CloseApplications=yes
CloseApplicationsFilter=*.exe,*.dll
RestartApplications=no
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
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: none; ValueName: "WindowsCompanion"; Flags: uninsdeletevalue dontcreatekey
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: none; ValueName: "HaCompanion"; Flags: uninsdeletevalue dontcreatekey

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Launch {#MyAppName}"; Flags: nowait postinstall skipifsilent

[Code]
const
  EVENT_MODIFY_STATE = $0002;
  SYNCHRONIZE = $00100000;
  WAIT_OBJECT_0 = 0;
  ShutdownTimeoutMs = 15000;
  AppWindowTitle = 'Windows Companion for Home Assistant';

function OpenEvent(DesiredAccess: LongWord; InheritHandle: Boolean;
  Name: String): THandle;
  external 'OpenEventW@kernel32.dll stdcall';
function SetEvent(Event: THandle): Boolean;
  external 'SetEvent@kernel32.dll stdcall';
function OpenProcess(DesiredAccess: LongWord; InheritHandle: Boolean;
  ProcessId: LongWord): THandle;
  external 'OpenProcess@kernel32.dll stdcall';
function WaitForSingleObject(Handle: THandle; Milliseconds: LongWord): LongWord;
  external 'WaitForSingleObject@kernel32.dll stdcall';
function CloseHandle(Handle: THandle): Boolean;
  external 'CloseHandle@kernel32.dll stdcall';
function GetWindowThreadProcessId(Window: HWND; var ProcessId: LongWord): LongWord;
  external 'GetWindowThreadProcessId@user32.dll stdcall';

function CloseRunningCompanions(OperationName: String; ConfirmClose: Boolean): Boolean;
var
  Window: HWND;
  ProcessId: LongWord;
  Event: THandle;
  ProcessHandle: THandle;
  PromptResult: Integer;
begin
  Result := True;
  Window := FindWindowByWindowName(AppWindowTitle);
  if Window = 0 then
    exit;

  if ConfirmClose then
  begin
    PromptResult := MsgBox(
      OperationName + ' needs to close Windows Companion gracefully before continuing.' +
      Chr(13) + Chr(10) + Chr(13) + Chr(10) +
      'Background reporting will stop, and the application will not be restarted automatically.',
      mbConfirmation, MB_YESNO);
    if PromptResult <> IDYES then
    begin
      Result := False;
      exit;
    end;
  end;

  while Window <> 0 do
  begin
    ProcessId := 0;
    GetWindowThreadProcessId(Window, ProcessId);
    if ProcessId = 0 then
    begin
      Result := False;
      exit;
    end;

    ProcessHandle := OpenProcess(SYNCHRONIZE, False, ProcessId);
    Event := OpenEvent(
      EVENT_MODIFY_STATE,
      False,
      'Local\WindowsCompanion.Shutdown.' + IntToStr(ProcessId));
    if (ProcessHandle = 0) or (Event = 0) then
    begin
      if Event <> 0 then
        CloseHandle(Event);
      if ProcessHandle <> 0 then
        CloseHandle(ProcessHandle);
      Result := False;
      exit;
    end;

    try
      if not SetEvent(Event) then
      begin
        Result := False;
        exit;
      end;

      if WaitForSingleObject(ProcessHandle, ShutdownTimeoutMs) <> WAIT_OBJECT_0 then
      begin
        Result := False;
        exit;
      end;
    finally
      CloseHandle(Event);
      CloseHandle(ProcessHandle);
    end;

    Window := FindWindowByWindowName(AppWindowTitle);
  end;
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  Result := '';
  if not CloseRunningCompanions('Setup', not WizardSilent) then
    Result :=
      'Windows Companion did not finish shutting down within 15 seconds. ' +
      'No process was terminated. Close it from the tray and retry Setup.';
end;

function InitializeUninstall(): Boolean;
begin
  Result := CloseRunningCompanions('Uninstall', not UninstallSilent);
  if not Result then
    MsgBox(
      'Windows Companion did not finish shutting down within 15 seconds. ' +
      'No process was terminated, and uninstall has been cancelled.',
      mbError, MB_OK);
end;
