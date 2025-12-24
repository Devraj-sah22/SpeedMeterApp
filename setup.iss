[Setup]
AppName=SpeedMeterApp
AppVersion=1.0
DefaultDirName={pf}\SpeedMeterApp
DefaultGroupName=SpeedMeterApp
OutputDir=.\Output
OutputBaseFilename=SpeedMeterApp_Setup
Compression=lzma2
SolidCompression=yes

[Files]
Source: "bin\Release\net8.0-windows\publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\SpeedMeterApp"; Filename: "{app}\SpeedMeterApp.exe"
Name: "{commondesktop}\SpeedMeterApp"; Filename: "{app}\SpeedMeterApp.exe"