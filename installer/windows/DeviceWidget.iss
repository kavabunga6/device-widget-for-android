#ifndef MyAppVersion
  #error MyAppVersion must be provided by build_windows_installer.ps1
#endif
#ifndef MyNumericVersion
  #error MyNumericVersion must be provided by build_windows_installer.ps1
#endif
#ifndef MySourceDir
  #error MySourceDir must be provided by build_windows_installer.ps1
#endif
#ifndef MyOutputDir
  #error MyOutputDir must be provided by build_windows_installer.ps1
#endif
#ifndef MyArchitecture
  #error MyArchitecture must be provided by build_windows_installer.ps1
#endif
#ifndef MyIconFile
  #error MyIconFile must be provided by build_windows_installer.ps1
#endif
#ifndef MyLicenseFile
  #error MyLicenseFile must be provided by build_windows_installer.ps1
#endif

#define MyAppName "Device Widget for Android"
#define MyAppExeName "DeviceWidget.exe"
#define MyPublisher "Device Widget contributors"
#define MyProjectUrl "https://github.com/kavabunga6/device-widget-for-android"

[Setup]
AppId={{7A9B8B24-7A93-4EE7-9606-5E9927A88931}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyPublisher}
AppPublisherURL={#MyProjectUrl}
AppSupportURL={#MyProjectUrl}/issues
AppUpdatesURL={#MyProjectUrl}/releases
DefaultDirName={localappdata}\Programs\Device Widget
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
OutputDir={#MyOutputDir}
OutputBaseFilename=DeviceWidget-for-Android-{#MyAppVersion}-{#MyArchitecture}-Setup
SetupIconFile={#MyIconFile}
LicenseFile={#MyLicenseFile}
UninstallDisplayIcon={app}\{#MyAppExeName}
UninstallDisplayName={#MyAppName}
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern dynamic
MinVersion=10.0.19041
CloseApplications=yes
CloseApplicationsFilter={#MyAppExeName}
RestartApplications=no
UsePreviousAppDir=yes
UsePreviousTasks=yes
SetupLogging=yes
VersionInfoVersion={#MyNumericVersion}
VersionInfoCompany={#MyPublisher}
VersionInfoDescription={#MyAppName} installer
VersionInfoProductName={#MyAppName}
VersionInfoProductVersion={#MyAppVersion}

#if MyArchitecture == "win-arm64"
ArchitecturesAllowed=arm64
ArchitecturesInstallIn64BitMode=arm64
#else
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
#endif

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "russian"; MessagesFile: "compiler:Languages\Russian.isl"

[CustomMessages]
english.desktopicon=Create a &desktop shortcut
russian.desktopicon=Создать ярлык на &рабочем столе
english.launchapp=Launch Device Widget for Android
russian.launchapp=Запустить Device Widget for Android

[Tasks]
Name: "desktopicon"; Description: "{cm:desktopicon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "{#MySourceDir}\*"; DestDir: "{app}"; Excludes: "*.pdb"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:launchapp}"; WorkingDir: "{app}"; Flags: nowait postinstall skipifsilent
