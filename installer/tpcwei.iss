#define MyAppName "TPC"
#define MyAppVersion "0.1.0"
#define MyAppPublisher "TPC"
#define MyAppExeName "TPC.exe"

[Setup]
AppId={{B556339B-831E-4B80-A0A5-51B4662ED2A3}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={autopf}\TPC
DefaultGroupName=TPC
DisableProgramGroupPage=yes
OutputDir=..\publish
OutputBaseFilename=TPCSetup
SetupIconFile=..\design\favicon.ico
Compression=lzma
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.19041

[Languages]
Name: "chinesesimp"; MessagesFile: "compiler:Languages\ChineseSimplified.isl"

[Tasks]
Name: "desktopicon"; Description: "Create desktop shortcut"; GroupDescription: "Additional tasks:"; Flags: unchecked
Name: "installservices"; Description: "Install background Agent as Windows Service (recommended)"; GroupDescription: "Background service:"

[Files]
Source: "..\publish\winui-win-x64\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\TPC"; Filename: "{app}\{#MyAppExeName}"; IconFilename: "{app}\favicon.ico"
Name: "{autodesktop}\TPC"; Filename: "{app}\{#MyAppExeName}"; IconFilename: "{app}\favicon.ico"; Tasks: desktopicon

[Run]
Filename: "{cmd}"; Parameters: "/C sc query TPCAgent >nul 2>&1 || sc create TPCAgent binPath= ""{app}\TPC.Agent.exe --service"" start= auto DisplayName= ""TPC Agent"""; StatusMsg: "Installing Agent service..."; Tasks: installservices; Flags: runhidden
Filename: "{cmd}"; Parameters: "/C sc start TPCAgent"; StatusMsg: "Starting Agent service..."; Tasks: installservices; Flags: runhidden
Filename: "{cmd}"; Parameters: "/C if exist ""{app}\drivers\wintun.dll"" (echo virtual lan driver available>""{app}\driver-status.txt"") else (echo Minecraft remote LAN mode is available>""{app}\driver-status.txt"")"; StatusMsg: "Checking virtual network component..."; Flags: runhidden
Filename: "{app}\{#MyAppExeName}"; Description: "Start TPC"; Flags: nowait postinstall skipifsilent

[UninstallRun]
Filename: "{cmd}"; Parameters: "/C sc stop TPCAgent"; Flags: runhidden; RunOnceId: "StopTPCAgent"
Filename: "{cmd}"; Parameters: "/C sc delete TPCAgent"; Flags: runhidden; RunOnceId: "DeleteTPCAgent"
