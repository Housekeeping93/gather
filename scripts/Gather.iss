#ifndef PackageRoot
  #error PackageRoot must point to the verified published application
#endif
#ifndef ReleaseOutput
  #error ReleaseOutput must point to the output folder
#endif

[Setup]
#ifdef QA
AppId={{83CC9359-8419-43CE-86E3-F7285EBC3C1B}
#else
AppId={{446932D6-C165-4CA9-94C3-73CD3988EB82}
#endif
AppName=Gather
AppVersion=0.10.0-dev.8
VersionInfoVersion=0.10.0.8
#ifdef QA
DefaultDirName={src}\qa-installer-app
#else
DefaultDirName={localappdata}\Programs\Gather
#endif
DefaultGroupName=Gather
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.22000
WizardStyle=modern
Compression=lzma2/fast
SolidCompression=yes
OutputDir={#ReleaseOutput}
#ifndef QA
OutputBaseFilename=Gather-Setup-0.10.0-dev.8
#endif
SetupIconFile={#PackageRoot}\Assets\Gather.ico
UninstallDisplayIcon={app}\Gather.exe
CloseApplications=yes
RestartApplications=no
AllowNoIcons=yes
#ifdef QA
CreateUninstallRegKey=no
OutputBaseFilename=Gather-Setup-QA
#endif

[Tasks]
Name: desktopicon; Description: "Create a desktop shortcut"; Flags: unchecked

[Files]
Source: "{#PackageRoot}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

#ifndef QA
[Icons]
Name: "{group}\Gather"; Filename: "{app}\Gather.exe"; WorkingDir: "{app}"
Name: "{autodesktop}\Gather"; Filename: "{app}\Gather.exe"; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
Filename: "{app}\Gather.exe"; Description: "Open Gather"; Flags: nowait postinstall skipifsilent unchecked
#endif

[Code]
function HasWebView2: Boolean;
var Version: String;
begin
  Result := (RegQueryStringValue(HKLM32, 'SOFTWARE\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}', 'pv', Version) and (Version <> '') and (Version <> '0.0.0.0')) or
    (RegQueryStringValue(HKCU, 'SOFTWARE\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}', 'pv', Version) and (Version <> '') and (Version <> '0.0.0.0'));
end;

function InitializeSetup: Boolean;
var ErrorCode: Integer;
begin
  Result := True;
#ifndef QA
  if not HasWebView2 then begin
    Result := False;
    if not WizardSilent then
      if MsgBox('Gather needs Microsoft Edge WebView2. Install it, then run this setup again. Open the Microsoft download page now?', mbInformation, MB_YESNO) = IDYES then
        ShellExec('open', 'https://developer.microsoft.com/microsoft-edge/webview2/', '', '', SW_SHOWNORMAL, ewNoWait, ErrorCode);
  end;
#endif
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var StartupCommand, InstalledCommand: String;
begin
#ifndef QA
  if CurUninstallStep = usUninstall then begin
    InstalledCommand := '"' + ExpandConstant('{app}\Gather.exe') + '"';
    if RegQueryStringValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Run', 'Gather', StartupCommand) then
      if (CompareText(StartupCommand, InstalledCommand) = 0) or
         (CompareText(Copy(StartupCommand, 1, Length(InstalledCommand) + 12), InstalledCommand + ' --data-dir ') = 0) then
        RegDeleteValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Run', 'Gather');
  end;
#endif
end;

// Uninstall removes this installation's startup entry, program files and shortcuts. Account profiles
// in LocalAppData\Gather remain available for reinstall or deliberate removal.

