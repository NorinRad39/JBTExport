; =============================================================================
;  Configuration de deploiement de JBTExport.
;
;  Seul fichier propre au projet : deploy.iss et deploy_update.ps1 viennent du
;  depot deploy-toolkit et se recopient tels quels.
;
;  Les chemins relatifs partent de ce dossier.
; =============================================================================


; --- Identite de l'application -----------------------------------------------

#define AppName "JBTExport"
#define AppSlug "JBTExport"
#define AppPublisher "Florent FABBRI"

#define ExeName "JBTExport.exe"

; Genere le 28/08/2026. A NE PLUS JAMAIS CHANGER : c'est ce qui permet a Inno de
; reconnaitre une installation existante et de la mettre a jour au lieu d'en
; empiler une seconde sur chaque poste.
#define AppId "{{B270F8F1-1AC5-4212-963B-29D42DA8DA46}"


; --- Ce qu'on empaquette ------------------------------------------------------

#define SourceDir "..\JBTExport\bin\Release"

#define IconFile "..\JBTExport\Logo export.ico"


; --- Publication --------------------------------------------------------------

; Chemin UNC et non une lettre de lecteur : un mappage est propre a la session, et
; sur un poste ou il manque les mises a jour cesseraient sans que personne ne s'en
; apercoive.
#define UpdateFolder "\\jbtec-be\meca$\topsolid\JBTExport"


; --- Options ------------------------------------------------------------------

; Aucune association de fichier : JBTExport est lance depuis TopSolid ou depuis
; son raccourci, il n'ouvre pas de document.

; Meme certificat que le JBT PDF Viewer -- meme editeur, et le MSIX de JBTExport
; signait deja avec celui-la. Depose dans le magasin TrustedPublisher de
; l'utilisateur ; il ne conditionne plus rien depuis l'abandon du MSIX, ou la
; signature etait exigee pour installer.
#define CertFile "cert\JBTExport.cer"

; Cle privee de signature du setup, lue par deploy_update.ps1 seulement. Non
; versionnee : sur un poste qui ne l'a pas, la signature est sautee et la
; publication aboutit quand meme.
#define CertPfx "cert\JBTExport.pfx"

; Paquet MSIX de la version precedente, desinstalle avant d'installer celle-ci.
; Sans cela les deux cohabitent, avec deux entrees au menu Demarrer.
#define MsixPackageName "ab1947a2-5ced-4484-a9a2-60587ebdcf73"
