; Instalador do Ilha (Inno Setup 6)
; Gerar:  ISCC.exe installer\Ilha.iss   (depois de "dotnet publish ... -o publish")

#define AppName "Ilha"
#define AppVersion "1.2.1"
#define AppExe "Ilha.exe"

; O instalador sÃ³ leva o Ilha.exe. Se houver DLL solta em publish\, o exe depende dela
; e nÃ£o abriria depois de instalado â€” entÃ£o nem gera o instalador.
#if FindFirst("..\publish\*.dll", 0) != 0
  #error HÃ¡ DLLs soltas em publish\. Apague a pasta publish e rode "dotnet publish -c Release -o publish" (o .csproj jÃ¡ embute tudo no exe).
#endif

[Setup]
AppId={{6F2A7C1E-3B5D-4E8A-9C21-7D4F0B8E1A55}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher={#AppName}
; Instala sÃ³ para o usuÃ¡rio atual: nÃ£o pede permissÃ£o de administrador.
PrivilegesRequired=lowest
DefaultDirName={localappdata}\Programs\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
OutputDir=..
OutputBaseFilename=Ilha-Setup-{#AppVersion}
SetupIconFile=..\Ilha.ico
UninstallDisplayIcon={app}\{#AppExe}
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.19041
; Fecha o Ilha se estiver aberto durante atualizaÃ§Ã£o/desinstalaÃ§Ã£o.
CloseApplications=force
RestartApplications=no

[Languages]
Name: "ptbr"; MessagesFile: "compiler:Languages\BrazilianPortuguese.isl"

[Tasks]
Name: "desktopicon"; Description: "Criar atalho na Ã¡rea de trabalho"; GroupDescription: "Atalhos:"; Flags: unchecked
Name: "startup"; Description: "Iniciar o Ilha junto com o Windows"; GroupDescription: "InicializaÃ§Ã£o:"

[Files]
Source: "..\publish\{#AppExe}"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\Fonts\Inter-LICENSE.txt"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExe}"
Name: "{group}\Desinstalar {#AppName}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Registry]
; Mesmo valor que a opÃ§Ã£o "Iniciar com o Windows" do app usa, entÃ£o os dois ficam em sincronia.
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "{#AppName}"; ValueData: """{app}\{#AppExe}"""; Tasks: startup; Flags: uninsdeletevalue
; Garante que a desinstalaÃ§Ã£o remova a entrada mesmo se ela foi ativada pelo prÃ³prio app.
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueName: "{#AppName}"; Flags: uninsdeletevalue dontcreatekey; Check: not WizardIsTaskSelected('startup')

[Run]
Filename: "{app}\{#AppExe}"; Description: "Abrir o {#AppName} agora"; Flags: nowait postinstall skipifsilent
; AtualizaÃ§Ã£o automÃ¡tica (o app roda o instalador com /VERYSILENT /AUTOUPDATE): reabre o Ilha no fim.
Filename: "{app}\{#AppExe}"; Flags: nowait; Check: IsAutoUpdate

[UninstallRun]
Filename: "{sys}\taskkill.exe"; Parameters: "/F /IM {#AppExe}"; Flags: runhidden; RunOnceId: "KillIlha"

[Code]
function IsAutoUpdate: Boolean;
var
  I: Integer;
begin
  Result := False;
  for I := 1 to ParamCount do
    if CompareText(ParamStr(I), '/AUTOUPDATE') = 0 then
      Result := True;
end;
