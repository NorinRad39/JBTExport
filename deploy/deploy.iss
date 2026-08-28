; =============================================================================
;  Script Inno Setup generique -- deploy-toolkit
;
;  Ne rien modifier ici : tout ce qui est propre au projet vit dans
;  deploy.config.iss, a cote de ce fichier.
;
;  Installation par utilisateur, dans un chemin fixe. C'est tout l'objet de ce
;  toolkit : un paquet MSIX s'installe sous WindowsApps dans un dossier qui
;  porte le numero de version, et tout raccourci ou automation pointant sur
;  l'executable casse a chaque mise a jour.
; =============================================================================

#include "deploy.config.iss"

; Version lue dans l'executable lui-meme : une seule source de verite, celle que
; l'application affiche et que update.xml annoncera.
#define AppVersion GetVersionNumbersString(AddBackslash(SourceDir) + ExeName)

; Nom technique : dossier d'installation, nom du setup, identifiant de type de
; fichier. A defaut, le nom affiche fait l'affaire -- mais des qu'il contient des
; espaces ou des tirets, mieux vaut les separer.
#ifndef AppSlug
  #define AppSlug AppName
#endif

#define ProgId AppSlug + ".Document"


[Setup]
AppId={#AppId}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher={#AppPublisher}
VersionInfoVersion={#AppVersion}

; Aucun droit administrateur, et un chemin d'installation impose : le laisser
; choisir a l'utilisateur reintroduirait exactement le probleme qu'on corrige.
PrivilegesRequired=lowest
DefaultDirName={localappdata}\{#AppSlug}
DisableDirPage=yes
DisableProgramGroupPage=yes
DefaultGroupName={#AppName}
UsePreviousAppDir=yes

OutputDir=Output
OutputBaseFilename={#AppSlug}-setup
SetupIconFile={#IconFile}
UninstallDisplayIcon={app}\{#ExeName}
UninstallDisplayName={#AppName}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern

; Une mise a jour est lancee par AutoUpdater alors que l'application vient de se
; fermer : si un processus traine, Inno propose de le fermer plutot que d'echouer
; sur un fichier verrouille.
CloseApplications=yes
RestartApplications=no


[Languages]
Name: "french"; MessagesFile: "compiler:Languages\French.isl"


[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
#ifdef CertFile
Source: "{#CertFile}"; DestDir: "{app}"; Flags: ignoreversion
#endif


[Icons]
; Le raccourci du bureau est pose ici, par l'installeur : c'est son travail, et
; il sait aussi le retirer a la desinstallation.
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#ExeName}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#ExeName}"


[Run]
#ifdef CertFile
; TrustedPublisher de l'utilisateur courant : aucun droit administrateur requis,
; et aucune invite, contrairement au magasin Root.
Filename: "{sys}\certutil.exe"; \
  Parameters: "-user -addstore TrustedPublisher ""{app}\{#ExtractFileName(CertFile)}"""; \
  Flags: runhidden; \
  StatusMsg: "Installation du certificat de l'editeur..."; \
  Check: CertificatAbsent
#endif

; Pas de skipifsilent : une mise a jour lancee par AutoUpdater installe en mode
; silencieux, et sans cette relance l'utilisateur se retrouverait devant un ecran
; vide, l'application ayant ete fermee pour etre remplacee.
Filename: "{app}\{#ExeName}"; Description: "Lancer {#AppName}"; Flags: nowait postinstall


[Code]

// ---------------------------------------------------------------------------
//  Associations de fichiers
//
//  Ecrites en code plutot qu'en section [Registry] pour deux raisons : la liste
//  d'extensions est une donnee de configuration qu'on parcourt, et il faut
//  prevenir le shell une fois les cles posees, sinon l'Explorateur continue
//  d'ignorer l'association jusqu'a la prochaine ouverture de session.
// ---------------------------------------------------------------------------

const
  SHCNE_ASSOCCHANGED = $08000000;
  SHCNF_IDLIST       = $0000;

procedure SHChangeNotify(wEventId, uFlags: Integer; dwItem1, dwItem2: Integer);
  external 'SHChangeNotify@shell32.dll stdcall';

function ListeExtensions(): TArrayOfString;
var
  Brut: String;
begin
  Brut := '';
#ifdef FileExtensions
  Brut := '{#FileExtensions}';
#endif
  if Brut = '' then
    SetArrayLength(Result, 0)
  else
    Result := StringSplitEx(Brut, [','], '"', stExcludeEmpty);
end;

procedure InscrireAssociations();
var
  Extensions: TArrayOfString;
  Cle, Ext: String;
  I: Integer;
begin
  Extensions := ListeExtensions();
  if GetArrayLength(Extensions) = 0 then
    Exit;

  Cle := 'Software\Classes\{#ProgId}';
  RegWriteStringValue(HKCU, Cle, '', '{#AppName}');
  RegWriteStringValue(HKCU, Cle + '\DefaultIcon', '', ExpandConstant('{app}\{#ExeName}') + ',0');
  RegWriteStringValue(HKCU, Cle + '\shell\open\command', '',
    '"' + ExpandConstant('{app}\{#ExeName}') + '" "%1"');

  for I := 0 to GetArrayLength(Extensions) - 1 do
  begin
    Ext := Trim(Extensions[I]);
    if Ext = '' then
      Continue;

    // La valeur par defaut de l'extension ne suffit pas sous Windows 10 et 11
    // quand un autre programme detient deja le type : le choix par defaut est
    // protege par UserChoice. OpenWithProgids ajoute au moins l'application au
    // menu "Ouvrir avec", et pour une extension inconnue du systeme la valeur
    // par defaut fait bien son effet.
    RegWriteStringValue(HKCU, 'Software\Classes\' + Ext, '', '{#ProgId}');
    RegWriteStringValue(HKCU, 'Software\Classes\' + Ext + '\OpenWithProgids', '{#ProgId}', '');
  end;

  SHChangeNotify(SHCNE_ASSOCCHANGED, SHCNF_IDLIST, 0, 0);
end;

procedure RetirerAssociations();
var
  Extensions: TArrayOfString;
  Ext: String;
  I: Integer;
begin
  Extensions := ListeExtensions();

  for I := 0 to GetArrayLength(Extensions) - 1 do
  begin
    Ext := Trim(Extensions[I]);
    if Ext = '' then
      Continue;

    RegDeleteValue(HKCU, 'Software\Classes\' + Ext + '\OpenWithProgids', '{#ProgId}');

    // On ne retire la valeur par defaut que si elle nous designe encore : un
    // autre programme a pu reprendre l'extension entre-temps, ce n'est pas a la
    // desinstallation de le desservir.
    if RegQueryStringValue(HKCU, 'Software\Classes\' + Ext, '', Ext) and (Ext = '{#ProgId}') then
      RegDeleteValue(HKCU, 'Software\Classes\' + Trim(Extensions[I]), '');
  end;

  RegDeleteKeyIncludingSubkeys(HKCU, 'Software\Classes\{#ProgId}');
  SHChangeNotify(SHCNE_ASSOCCHANGED, SHCNF_IDLIST, 0, 0);
end;


// ---------------------------------------------------------------------------
//  Certificat
// ---------------------------------------------------------------------------

#ifdef CertFile
function CertificatAbsent(): Boolean;
var
  Code: Integer;
begin
  // certutil renvoie un code non nul quand le certificat n'est pas trouve dans
  // le magasin : on evite ainsi de le reinstaller a chaque mise a jour.
  Result := True;
  if Exec(ExpandConstant('{sys}\certutil.exe'),
          '-user -verifystore TrustedPublisher "{#AppPublisher}"',
          '', SW_HIDE, ewWaitUntilTerminated, Code) then
    Result := (Code <> 0);
end;
#endif


// ---------------------------------------------------------------------------
//  Migration depuis MSIX
// ---------------------------------------------------------------------------

#ifdef MsixPackageName
procedure RetirerPaquetMsix();
var
  Code: Integer;
begin
  // Desinstallation du paquet de l'utilisateur courant uniquement : pas de droit
  // administrateur, et rien n'est touche chez les autres sessions du poste.
  // L'echec est sans consequence : il signifie simplement qu'aucun paquet
  // n'etait installe.
  Exec(ExpandConstant('{sys}\WindowsPowerShell\v1.0\powershell.exe'),
       '-NoProfile -ExecutionPolicy Bypass -Command "Get-AppxPackage -Name ''{#MsixPackageName}'' | Remove-AppxPackage"',
       '', SW_HIDE, ewWaitUntilTerminated, Code);
end;
#endif


// ---------------------------------------------------------------------------
//  Enchainement
// ---------------------------------------------------------------------------

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssInstall then
  begin
#ifdef MsixPackageName
    RetirerPaquetMsix();
#endif
  end;

  if CurStep = ssPostInstall then
    InscrireAssociations();
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usUninstall then
    RetirerAssociations();
end;
