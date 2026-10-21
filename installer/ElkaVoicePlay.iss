#ifndef SourceDir
  #error SourceDir must be a clean game-only publish folder.
#endif
#ifndef OutputDir
  #error OutputDir is required.
#endif
#if Ver < EncodeVer(6, 6, 0)
  #error Inno Setup 6.6 or newer is required.
#endif
#define AppName "Elka VoicePlay"
#define AppVersion "0.7.0-preview.1"
#define ExeName "ElkaVoicePlay.exe"
#define RepoURL "https://github.com/torment78/ElkaVoicePlay"

[Setup]
#ifdef InstallerTest
AppId=ElkaVoicePlayInstallerSmokeTest
OutputBaseFilename=ElkaVoicePlay-0.7.0-installer-test
PrivilegesRequired=lowest
DefaultGroupName=ElkaVoicePlayInstallerSmokeTest
#else
AppId={{72439C56-9159-4A03-9FB4-C9F3305706F4}
AppMutex=Local\ElkaSoft.ElkaVoicePlay
OutputBaseFilename=ElkaVoicePlay-0.7.0-setup-win-x64
PrivilegesRequired=admin
PrivilegesRequiredOverridesAllowed=commandline
DefaultGroupName=ElkaSoft
#endif
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher=ElkaSoft
AppPublisherURL={#RepoURL}
AppSupportURL={#RepoURL}/issues
AppUpdatesURL={#RepoURL}/releases
VersionInfoVersion=0.7.0.0
VersionInfoProductName={#AppName}
VersionInfoDescription={#AppName} preview installer (unsigned)
DefaultDirName={autopf}\ElkaSoft\{#AppName}
DisableDirPage=no
DisableProgramGroupPage=yes
DisableWelcomePage=no
UsePreviousAppDir=yes
UsePreviousTasks=yes
OutputDir={#OutputDir}
SetupIconFile=..\graphics\ElkaVoicePlay.ico
WizardImageFile=..\graphics\voiceplay-vertical.png
WizardSmallImageFile=..\src\ElkaVoicePlay.App\Assets\ElkaVoicePlay.png
WizardStyle=modern dark polar includetitlebar
WizardSizePercent=120,120
Compression=lzma2/max
SolidCompression=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.22000
UninstallDisplayIcon={app}\{#ExeName}
UninstallDisplayName={#AppName} (Preview)
#ifdef InstallerTest
CloseApplications=no
#else
CloseApplications=yes
CloseApplicationsFilter={#ExeName}
#endif
RestartApplications=no
SignedUninstaller=no

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Additional icons:"

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
#ifdef InstallerTest
Name: "{autoprograms}\ElkaVoicePlayInstallerSmokeTest\Elka VoicePlay Test"; Filename: "{app}\{#ExeName}"; WorkingDir: "{app}"
Name: "{autodesktop}\Elka VoicePlay Installer Test"; Filename: "{app}\{#ExeName}"; WorkingDir: "{app}"; Tasks: desktopicon
#else
Name: "{autoprograms}\ElkaSoft\{#AppName}"; Filename: "{app}\{#ExeName}"; WorkingDir: "{app}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#ExeName}"; WorkingDir: "{app}"; Tasks: desktopicon
#endif

[Run]
#ifndef InstallerTest
Filename: "{app}\{#ExeName}"; Description: "Launch {#AppName}"; Flags: nowait postinstall skipifsilent runasoriginaluser
#endif

[UninstallDelete]
; Retain profiles, transcripts, downloaded components and unrelated user files.
Type: dirifempty; Name: "{app}"

[Code]
procedure FitPortrait(Image: TBitmapImage);
var ArtworkHeight: Integer;
begin
  { Keep the full 2:3 artwork visible, including its title and footer. }
  ArtworkHeight := (Image.Width * 3) div 2;
  Image.Top := Image.Top + (Image.Height - ArtworkHeight) div 2;
  Image.Height := ArtworkHeight;
end;

procedure InitializeWizard;
begin
  FitPortrait(WizardForm.WizardBitmapImage);
  FitPortrait(WizardForm.WizardBitmapImage2);
  WizardForm.WelcomeLabel1.Caption := 'Elka VoicePlay';
  WizardForm.WelcomeLabel2.Caption :=
    'Hands-free chat for games' + #13#10#13#10 +
    'Game chat and Whisper Base are included. Hold a button, speak, then release it to type into your game.' + #13#10#13#10 +
    'Transcribe audio is an optional download inside the app. Medium, Large-v3 and extra speaker/GPU support are optional too.' + #13#10#13#10 +
    'Windows 11 x64  |  Unsigned preview';
end;
