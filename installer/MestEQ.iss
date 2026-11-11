; Mest EQ kurulum betiği
; Equalizer APO'yu (GPLv2) sessizce kurar, yapılandırma klasörüne yazma izni verir,
; config.txt'yi Mest'e bağlar ve cihaz seçiciyi bir kez açar.

#define AppName "Mest EQ"
#define AppVersion "1.0.0"
#define EapoSetup "EqualizerAPO-x64-1.4.2.exe"

[Setup]
AppId={{9B3E6C2A-5F1D-4C8B-A7E2-3D4F5A6B7C8D}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher=Mest
DefaultDirName={autopf}\Mest EQ
DefaultGroupName=Mest EQ
DisableProgramGroupPage=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequired=admin
MinVersion=10.0.17763
OutputDir=Output
OutputBaseFilename=MestEQ-Setup
SetupIconFile=..\MestEQ\Assets\mest.ico
UninstallDisplayIcon={app}\MestEQ.exe
WizardStyle=modern
Compression=lzma2/ultra64
SolidCompression=yes
CloseApplications=yes
LicenseFile=..\licenses\KURULUM-NOTU.txt

[Languages]
Name: "turkish"; MessagesFile: "compiler:Languages\Turkish.isl"

[Messages]
turkish.FinishedLabel=Mest kuruldu. EQ etkisini duymuyorsan bilgisayarı bir kez yeniden başlat; ses motoru o zaman Dawn Pro'ya tam olarak bağlanır.

[Tasks]
Name: "autostart"; Description: "Windows açılışında arka planda başlat (şarkıya göre otomatik geçiş için önerilir)"
Name: "desktopicon"; Description: "Masaüstüne kısayol koy"; Flags: unchecked

[Files]
Source: "..\publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "..\licenses\*"; DestDir: "{app}\licenses"; Flags: ignoreversion
Source: "..\deps\{#EapoSetup}"; Flags: dontcopy

[Icons]
Name: "{autoprograms}\Mest EQ"; Filename: "{app}\MestEQ.exe"
Name: "{autodesktop}\Mest EQ"; Filename: "{app}\MestEQ.exe"; Tasks: desktopicon
Name: "{commonstartup}\Mest EQ"; Filename: "{app}\MestEQ.exe"; Parameters: "--tray"; Tasks: autostart

[Run]
Filename: "{app}\MestEQ.exe"; Description: "Mest'i şimdi aç"; Flags: nowait postinstall skipifsilent runasoriginaluser

[UninstallRun]
Filename: "{sys}\taskkill.exe"; Parameters: "/IM MestEQ.exe /F"; Flags: runhidden; RunOnceId: "KillMest"

[Code]
function GetEapoDir(): String;
var
  s: String;
begin
  if RegQueryStringValue(HKLM64, 'SOFTWARE\EqualizerAPO', 'InstallPath', s) and (s <> '') then
    Result := s
  else
    Result := ExpandConstant('{commonpf64}\EqualizerAPO');
end;

function GetEapoConfigDir(): String;
var
  s: String;
begin
  if RegQueryStringValue(HKLM64, 'SOFTWARE\EqualizerAPO', 'ConfigPath', s) and (s <> '') then
    Result := s
  else
    Result := GetEapoDir() + '\config';
end;

function IsEapoInstalled(): Boolean;
begin
  Result := RegValueExists(HKLM64, 'SOFTWARE\EqualizerAPO', 'InstallPath') or
            FileExists(GetEapoDir() + '\Editor.exe');
end;

procedure InstallEapo();
var
  rc: Integer;
begin
  WizardForm.StatusLabel.Caption := 'Ses motoru (Equalizer APO) kuruluyor...';
  ExtractTemporaryFile('{#EapoSetup}');
  if not Exec(ExpandConstant('{tmp}\{#EapoSetup}'), '/S', '', SW_SHOW, ewWaitUntilTerminated, rc) then
    MsgBox('Equalizer APO kurulamadı (hata ' + IntToStr(rc) + '). Mest çalışır ama ses işlenmez. Kurulumu yeniden deneyebilirsin.',
           mbError, MB_OK);
end;

procedure LinkConfig();
var
  rc: Integer;
  cfg: String;
begin
  cfg := GetEapoConfigDir();
  ForceDirectories(cfg);
  { Windows kullanıcılarının (S-1-5-32-545) yapılandırma klasörüne yazabilmesi: Mest yönetici izni olmadan ayar yazar. }
  Exec(ExpandConstant('{sys}\icacls.exe'), '"' + cfg + '" /grant *S-1-5-32-545:(OI)(CI)M /T /C /Q', '', SW_HIDE, ewWaitUntilTerminated, rc);

  if FileExists(cfg + '\config.txt') and not FileExists(cfg + '\config.mest-yedek.txt') then
    FileCopy(cfg + '\config.txt', cfg + '\config.mest-yedek.txt', True);
  SaveStringToFile(cfg + '\config.txt', '# Bu dosya Mest EQ tarafindan yonetiliyor. Onceki icerik: config.mest-yedek.txt' + #13#10 + 'Include: mest.txt' + #13#10, False);
  if not FileExists(cfg + '\mest.txt') then
    SaveStringToFile(cfg + '\mest.txt', '# Mest EQ ilk acilista bu dosyayi doldurur.' + #13#10, False);
end;

procedure OpenDeviceSelector();
var
  rc: Integer;
  selector: String;
begin
  selector := GetEapoDir() + '\DeviceSelector.exe';
  if not FileExists(selector) then
    selector := GetEapoDir() + '\Configurator.exe';
  if not FileExists(selector) then
    Exit;
  MsgBox('Şimdi ses motorunun cihaz seçicisi açılacak.' + #13#10 + #13#10 +
         'Listede Moondrop Dawn Pro cihazını işaretle ve onayla.' + #13#10 +
         'Zaten işaretliyse pencereyi kapatman yeterli.' + #13#10 + #13#10 +
         'Bu adım sadece bir kez yapılır.', mbInformation, MB_OK);
  Exec(selector, '', '', SW_SHOW, ewWaitUntilTerminated, rc);
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssPostInstall then
  begin
    if not IsEapoInstalled() then
      InstallEapo();
    LinkConfig();
    if not WizardSilent() then
      OpenDeviceSelector();
  end;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  cfg: String;
begin
  if CurUninstallStep = usPostUninstall then
  begin
    cfg := GetEapoConfigDir();
    if FileExists(cfg + '\config.mest-yedek.txt') then
    begin
      DeleteFile(cfg + '\config.txt');
      RenameFile(cfg + '\config.mest-yedek.txt', cfg + '\config.txt');
    end
    else
      SaveStringToFile(cfg + '\config.txt', '', False);
    DeleteFile(cfg + '\mest.txt');
  end;
end;
