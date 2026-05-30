; Inno Setup Script for ArayCode
; Build from Release folder: iscc aray-code.iss

#define MyAppName "ArayCode"
#define MyAppVersion "1.0.0"
#define MyAppPublisher "ArayCode"
#define MyAppURL ""
#define MyAppExeName "aray-code.exe"

[Setup]
AppId={{8A3E4B2C-5D6F-4E8A-9B1C-2D3E4F5A6B7C}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
AppUpdatesURL={#MyAppURL}
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
AllowNoIcons=yes
OutputDir=installer
OutputBaseFilename=ArayCode-Setup
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
SetupIconFile=resources\app_icon.ico
PrivilegesRequired=admin

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "..\..\src\ArayCode\bin\Release\net10.0\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\Uninstall {#MyAppName}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent

[Registry]
; Add app directory to the system PATH variable safely
Root: HKLM; Subkey: "SYSTEM\CurrentControlSet\Control\Session Manager\Environment"; ValueType: expandsz; ValueName: "Path"; ValueData: "{olddata};{app}"; Flags: preservestringtype dontcreatekey

[UninstallDelete]
; Clean up the generated batch file shortcut and app data folders
Type: files; Name: "{app}\aray.bat"
Type: filesandordirs; Name: "{localappdata}\ArayCode"
Type: filesandordirs; Name: "{userappdata}\ArayCode"

[Code]
function IsDotNet10Installed: Boolean;
var
  ResultCode: Integer;
begin
  Result := Exec('dotnet', '--list-runtimes', '', SW_HIDE, ewWaitUntilTerminated, ResultCode) and (ResultCode = 0);
  if Result then
  begin
    // Check version via dotnet --list-runtimes output if needed
    Result := True;
  end;
end;

procedure CreateArayAlias;
var
  BatchPath: String;
  Lines: TArrayOfString;
begin
  BatchPath := ExpandConstant('{app}\aray.bat');
  SetArrayLength(Lines, 2);
  Lines[0] := '@echo off';
  Lines[1] := '"' + ExpandConstant('{app}\{#MyAppExeName}') + '" %*';
  
  if not SaveStringsToFile(BatchPath, Lines, False) then
  begin
    Log('Failed to create aray.bat alias.');
  end;
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssPostInstall then
  begin
    CreateArayAlias;
  end;
end;

procedure InitializeWizard;
begin
  if not IsDotNet10Installed then
  begin
    MsgBox('ArayCode requires .NET 10.0 runtime. Please install it from https://dotnet.microsoft.com/download/dotnet/10.0 before continuing.', mbError, MB_OK);
    WizardForm.Close;
  end;
end;