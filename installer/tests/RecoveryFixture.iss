; Isolated install: no Pickets shortcuts, startup entry, layout data, or application mutex.
#define MyAppExeName "recovery.cmd"
[Setup]
AppId=PicketsRecoveryGateTest
AppName=Pickets Recovery Gate Test
AppVersion=1.0
DefaultDirName={localappdata}\PicketsRecoveryGateTest
PrivilegesRequired=lowest
Uninstallable=yes
CreateAppDir=yes
OutputBaseFilename=RecoveryFixture
UninstallDisplayName=Pickets Recovery Gate Test

[Files]
Source: "recovery.cmd"; DestDir: "{app}"

[Code]
#include "..\UninstallRecovery.iss"
