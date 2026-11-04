; Inno Setup 6 script. Build with installer\build-installer.ps1 (it publishes the exe first).
; Output: installer\Output\AlienHelper-Setup-<version>.exe

#ifndef AppVersion
  #define AppVersion "0.1.0"
#endif
#ifndef Channel
  #define Channel "prod"
#endif
; the test channel (build-installer.ps1 on the test branches) installs next to prod: another AppId, folder, exe and
; settings folder; AppGuid and AppName must match AppId.cs
#if Channel == "test"
  #define AppName "AlienHelperTest"
  #define AppTitle "Alien Helper Test"
  #define AppGuid "B2D84E6A-5C1F-4A93-8E7B-3F6D0C9A2E15"
#else
  #define AppName "AlienHelper"
  #define AppTitle "Alien Helper"
  #define AppGuid "6F3C2B1E-8A47-4D5C-9E21-A1E7F0B4C3D9"
#endif
#define AppExe AppName + ".exe"

[Setup]
; keep AppId unchanged between versions: upgrades and the uninstaller find the install by it
AppId={{{#AppGuid}}
AppName={#AppTitle}
AppVersion={#AppVersion}
AppVerName={#AppTitle} {#AppVersion}
AppPublisher=Kirill Getmanskiy
AppCopyright=Copyright (C) 2026 Kirill Getmanskiy
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppTitle}
DisableProgramGroupPage=yes
; the app needs admin rights for hardware access anyway
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0
LicenseFile=..\LICENSE
SetupIconFile=..\src\AlienHelper\Assets\app.ico
UninstallDisplayIcon={app}\{#AppExe}
UninstallDisplayName={#AppTitle}
OutputDir=Output
OutputBaseFilename={#AppName}-Setup-{#AppVersion}
#ifdef Sign
; Microsoft Store accepts only signed installers; build-installer.ps1 -SignCmd defines the tool
SignTool=signtool
SignedUninstaller=yes
#endif
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
; English by default instead of the Windows UI language
LanguageDetectionMethod=none
ShowLanguageDialog=yes
; the app is closed by [Code] below; the Restart Manager can't close a tray app cleanly
CloseApplications=no

[Languages]
; the first entry is the default; others can be picked in the language dialog
Name: "en"; MessagesFile: "compiler:Default.isl"
Name: "ru"; MessagesFile: "compiler:Languages\Russian.isl"
Name: "fr"; MessagesFile: "compiler:Languages\French.isl"
Name: "es"; MessagesFile: "compiler:Languages\Spanish.isl"
Name: "ja"; MessagesFile: "compiler:Languages\Japanese.isl"

[CustomMessages]
ru.Disclaimer=Программа напрямую управляет режимами питания, вентиляторами и подсветкой через недокументированные интерфейсы прошивки. Она предоставляется «как есть», без гарантий; вы используете её на свой страх и риск.%n%nAlienHelper не связан с Dell Inc. Alienware — товарный знак Dell Inc.
en.Disclaimer=The program controls power modes, fans and lighting directly through undocumented firmware interfaces. It is provided "as is", without warranty; you use it at your own risk.%n%nAlienHelper is not affiliated with Dell Inc. Alienware is a trademark of Dell Inc.
fr.Disclaimer=The program controls power modes, fans and lighting directly through undocumented firmware interfaces. It is provided "as is", without warranty; you use it at your own risk.%n%nAlienHelper is not affiliated with Dell Inc. Alienware is a trademark of Dell Inc.
es.Disclaimer=The program controls power modes, fans and lighting directly through undocumented firmware interfaces. It is provided "as is", without warranty; you use it at your own risk.%n%nAlienHelper is not affiliated with Dell Inc. Alienware is a trademark of Dell Inc.
ja.Disclaimer=The program controls power modes, fans and lighting directly through undocumented firmware interfaces. It is provided "as is", without warranty; you use it at your own risk.%n%nAlienHelper is not affiliated with Dell Inc. Alienware is a trademark of Dell Inc.
ru.DeleteSettings=Удалить также настройки {#AppTitle} (%APPDATA%\{#AppName})?
en.DeleteSettings=Also delete {#AppTitle} settings (%APPDATA%\{#AppName})?
fr.DeleteSettings=Also delete {#AppTitle} settings (%APPDATA%\{#AppName})?
es.DeleteSettings=Also delete {#AppTitle} settings (%APPDATA%\{#AppName})?
ja.DeleteSettings=Also delete {#AppTitle} settings (%APPDATA%\{#AppName})?

en.GetPawnIO=Open the PawnIO page (needed for per-core temperatures and total power)
ru.GetPawnIO=Открыть страницу PawnIO (нужен для температур ядер и общего потребления)
fr.GetPawnIO=Ouvrir la page PawnIO (nécessaire pour les températures par cœur et la puissance totale)
es.GetPawnIO=Abrir la página de PawnIO (necesario para las temperaturas por núcleo y la potencia total)
ja.GetPawnIO=PawnIO のページを開く (コアごとの温度と総消費電力に必要)

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "app\{#AppExe}"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\LICENSE"; DestDir: "{app}"; DestName: "LICENSE.txt"; Flags: ignoreversion
Source: "..\NOTICE.md"; DestDir: "{app}"; DestName: "NOTICE.txt"; Flags: ignoreversion
Source: "app\THIRD-PARTY-NOTICES.txt"; DestDir: "{app}"; Flags: ignoreversion
Source: "app\licenses\*"; DestDir: "{app}\licenses"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\{#AppTitle}"; Filename: "{app}\{#AppExe}"
Name: "{autodesktop}\{#AppTitle}"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Run]
; the installer is elevated, so the app starts without a second UAC prompt
Filename: "{app}\{#AppExe}"; Description: "{cm:LaunchProgram,{#AppName}}"; Flags: nowait postinstall skipifsilent
; the app installs no driver: the optional PawnIO driver comes from its own site
Filename: "https://pawnio.eu/"; Description: "{cm:GetPawnIO}"; Flags: shellexec postinstall skipifsilent unchecked nowait runasoriginaluser; Check: not PawnIoInstalled

[UninstallRun]
; scheduled tasks the app creates for autostart and for starting without UAC
Filename: "{sys}\schtasks.exe"; Parameters: "/Delete /F /TN {#AppName}"; Flags: runhidden; RunOnceId: "DelTaskLogon"
Filename: "{sys}\schtasks.exe"; Parameters: "/Delete /F /TN {#AppName}-NoUAC"; Flags: runhidden; RunOnceId: "DelTaskNoUac"

[Code]
var
  DisclaimerPage: TOutputMsgWizardPage;

function IsAppRunning(): Boolean;
var
  Code: Integer;
begin
  // find.exe returns 0 when the image name is in the tasklist output
  Result := Exec(ExpandConstant('{cmd}'), '/C tasklist /NH /FI "IMAGENAME eq {#AppExe}" | find /I "{#AppExe}"',
    '', SW_HIDE, ewWaitUntilTerminated, Code) and (Code = 0);
end;

// Asks a running instance to quit normally (it resets the fan boost on exit), then kills it if it hangs.
procedure CloseApp(const Exe: String);
var
  Code, I: Integer;
begin
  if not IsAppRunning() then Exit;
  if FileExists(Exe) then
    Exec(Exe, '--exit', '', SW_HIDE, ewWaitUntilTerminated, Code);
  for I := 1 to 20 do
  begin
    if not IsAppRunning() then Exit;
    Sleep(250);
  end;
  Exec(ExpandConstant('{sys}\taskkill.exe'), '/F /IM {#AppExe}', '', SW_HIDE, ewWaitUntilTerminated, Code);
  Sleep(500);
end;

procedure InitializeWizard();
begin
  DisclaimerPage := CreateOutputMsgPage(wpLicense, SetupMessage(msgWizardInfoBefore),
    SetupMessage(msgInfoBeforeLabel), CustomMessage('Disclaimer'));
end;

const
  // Inno writes the uninstall entry under the AppId ({...}) plus "_is1"
  UninstallKey = 'Software\Microsoft\Windows\CurrentVersion\Uninstall\{' + '{#AppGuid}' + '}_is1';
  // scheduled tasks the app creates (see Autostart.cs); the old uninstaller deletes them
  TaskNames = '{#AppName},{#AppName}-NoUAC';

var
  TasksSaved: Boolean;

function TaskFile(const Name: String): String;
begin
  Result := ExpandConstant('{tmp}\task-' + Name + '.xml');
end;

procedure RunPowerShell(const Script: String);
var
  Code: Integer;
begin
  Exec(ExpandConstant('{sys}\WindowsPowerShell\v1.0\powershell.exe'),
    '-NoProfile -NonInteractive -ExecutionPolicy Bypass -Command "' + Script + '"', '', SW_HIDE, ewWaitUntilTerminated, Code);
end;

// Saves the autostart / no-UAC tasks so they survive removing the old version; RestoreTasks points them at
// the installed exe, or they would keep starting whatever copy created them.
procedure SaveTasks();
var
  Names: TArrayOfString;
  I: Integer;
begin
  Names := StringSplit(TaskNames, [','], stAll);
  for I := 0 to GetArrayLength(Names) - 1 do
    RunPowerShell('$t = Get-ScheduledTask -TaskPath ''\'' -TaskName ''' + Names[I] + ''' -ErrorAction SilentlyContinue; ' +
      'if ($t) { Export-ScheduledTask -InputObject $t | Set-Content -Encoding Unicode -LiteralPath ''' + TaskFile(Names[I]) + ''' }');
  TasksSaved := True;
end;

procedure RestoreTasks();
var
  Names: TArrayOfString;
  I: Integer;
begin
  if not TasksSaved then Exit;
  Names := StringSplit(TaskNames, [','], stAll);
  for I := 0 to GetArrayLength(Names) - 1 do
    if FileExists(TaskFile(Names[I])) then
      RunPowerShell('Register-ScheduledTask -TaskPath ''\'' -TaskName ''' + Names[I] + ''' -Force ' +
        '-Xml ((Get-Content -Raw -LiteralPath ''' + TaskFile(Names[I]) + ''') -replace ''<Command>.*?</Command>'', ''<Command>' +
        ExpandConstant('{app}\{#AppExe}') + '</Command>'') | Out-Null');
end;

// Removes an installed older (or the same) version with its own uninstaller, shortcuts included,
// so a renamed shortcut ("AlienHelper" -> "Alien Helper") doesn't stay behind. Settings are kept.
procedure UninstallPrevious();
var
  Uninst, Dir: String;
  Code, I: Integer;
begin
  if not RegQueryStringValue(HKA, UninstallKey, 'UninstallString', Uninst) then Exit;
  Uninst := RemoveQuotes(Uninst);
  if not FileExists(Uninst) then Exit;
  if RegQueryStringValue(HKA, UninstallKey, 'InstallLocation', Dir) then
    CloseApp(AddBackslash(RemoveQuotes(Dir)) + '{#AppExe}');
  SaveTasks();
  Exec(Uninst, '/VERYSILENT /SUPPRESSMSGBOXES /NORESTART', '', SW_HIDE, ewWaitUntilTerminated, Code);
  // the uninstaller finishes in a second process: wait until it has removed itself
  for I := 1 to 60 do
  begin
    if not FileExists(Uninst) then Break;
    Sleep(250);
  end;
end;

function PawnIoInstalled(): Boolean;
begin
  Result := RegKeyExists(HKLM, 'SYSTEM\CurrentControlSet\Services\PawnIO');
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  CloseApp(ExpandConstant('{app}\{#AppExe}'));
  UninstallPrevious();
  Result := '';
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssPostInstall then RestoreTasks();
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usUninstall then
    CloseApp(ExpandConstant('{app}\{#AppExe}'));
  if (CurUninstallStep = usPostUninstall) and not UninstallSilent() and
     DirExists(ExpandConstant('{userappdata}\{#AppName}')) and
     (MsgBox(CustomMessage('DeleteSettings'), mbConfirmation, MB_YESNO or MB_DEFBUTTON2) = IDYES) then
    DelTree(ExpandConstant('{userappdata}\{#AppName}'), True, True, True);
end;
