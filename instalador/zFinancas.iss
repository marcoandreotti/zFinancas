; ---------------------------------------------------------------------------
; Instalador do zFinancas (Inno Setup 6)
;
; Como gerar:
;   1. .\build.ps1 -Publish        (cria .tmp\publicacao\win-x64 com o app)
;   2. Instale o Inno Setup 6 ou 7: https://jrsoftware.org/isdl.php
;   3. Compile este script:
;        & "C:\Program Files (x86)\Inno Setup 6\ISCC.exe" instalador\zFinancas.iss
;        (ou "C:\Program Files\Inno Setup 7\ISCC.exe" no Inno 7)
;
; Resultado: instalador\saida\zFinancas-Setup-<versao>.exe
;
; Observações de projeto:
;   - O app NÃO grava nada na pasta de instalação: banco, logs e backups ficam em
;     %LOCALAPPDATA% (ou na pasta escolhida em Configurações). Por isso funciona em
;     Program Files sem exigir permissão de escrita para o usuário.
;   - Instalação por usuário (PrivilegesRequired=lowest) não pede administrador;
;     troque para "admin" se quiser instalar para todos os usuários da máquina.
; ---------------------------------------------------------------------------

#define NomeApp        "zFinancas"
#define VersaoApp      "1.0.0"
#define Fabricante     "zFinancas"
#define Executavel     "zFinancas.App.exe"
#define PastaPublicada "..\.tmp\publicacao\win-x64"

[Setup]
AppId={{8E3C1D52-6B7A-4B4A-9C2E-5D8C7A1F4B10}
AppName={#NomeApp}
AppVersion={#VersaoApp}
AppPublisher={#Fabricante}
DefaultDirName={autopf}\{#NomeApp}
DefaultGroupName={#NomeApp}
DisableProgramGroupPage=yes
OutputDir=saida
OutputBaseFilename={#NomeApp}-Setup-{#VersaoApp}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
UninstallDisplayName={#NomeApp}
; Ícone do instalador (gerado pelo resizetizer no build)
SetupIconFile=zFinancas.ico

[Languages]
Name: "brazilianportuguese"; MessagesFile: "compiler:Languages\BrazilianPortuguese.isl"

[Tasks]
Name: "desktopicon"; Description: "Criar atalho na Área de Trabalho"; GroupDescription: "Atalhos:"

[Files]
; Tudo o que o publish gerou (self-contained: não exige .NET instalado).
Source: "{#PastaPublicada}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#NomeApp}"; Filename: "{app}\{#Executavel}"
Name: "{group}\Desinstalar {#NomeApp}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#NomeApp}"; Filename: "{app}\{#Executavel}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#Executavel}"; Description: "Abrir o {#NomeApp}"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
; Não removemos dados do usuário (%LOCALAPPDATA%) na desinstalação: banco e backups ficam.
Type: filesandordirs; Name: "{app}\App_Data"
