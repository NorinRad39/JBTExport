<#
.SYNOPSIS
    Compile une application, fabrique son programme d'installation Inno Setup et le publie sur un
    partage réseau avec le fichier update.xml attendu par AutoUpdater.NET.

.DESCRIPTION
    Un seul geste pour livrer une version :

        .\deploy_update.ps1

    Le script construit l'application, compile deploy.iss avec ISCC, dépose le setup sur le partage
    et y régénère update.xml. Les postes prennent la mise à jour au lancement suivant.

    Tout ce qui est propre au projet vit dans deploy.config.iss, à côté de ce script — nom, exécutable,
    dossier de sortie, partage réseau. Aucun chemin n'est codé en dur ici, et rien n'est écrit deux
    fois : le .iss et ce script lisent la même configuration.

    La version n'est jamais inventée : elle est lue dans l'exécutable compilé, celui-là même qui part
    dans le setup. Une version annoncée qui ne correspond pas au binaire livré, et les postes se
    remettent à jour en boucle ou plus du tout.

.PARAMETER ConfigPath
    Le deploy.config.iss du projet. Par défaut : celui situé à côté de ce script.

.PARAMETER SolutionPath
    Solution à construire. Par défaut : la première trouvée en remontant depuis le projet.
    Passer par la solution n'est pas un détail — c'est elle qui fait correspondre les plateformes
    entre les projets.

.PARAMETER ProjectName
    Nom du projet à construire dans la solution. Par défaut : déduit du dossier de SourceDir.

.PARAMETER UpdateFolder
    Dossier réseau de publication. Par défaut : la valeur UpdateFolder du fichier de configuration.

.PARAMETER Configuration
    Configuration MSBuild. Release par défaut.

.PARAMETER Platform
    Plateforme MSBuild. « Any CPU » par défaut.

.PARAMETER CertificateFile
    Fichier .pfx servant à signer le setup. Sans lui, le setup n'est pas signé — ce qui n'empêche
    rien : une installation par utilisateur n'exige aucune signature.

.PARAMETER ChangelogUrl
    Adresse de la page de nouveautés affichée par AutoUpdater. Par défaut : changelog.html s'il est
    présent dans le dossier de publication, sinon rien.

.PARAMETER InstallerArgs
    Arguments passés au setup par AutoUpdater lors d'une mise à jour.

.PARAMETER Mandatory
    Mise à jour imposée : ni « Plus tard » ni « Ignorer ». Vrai par défaut.

.PARAMETER SkipBuild
    Ne pas reconstruire l'application, empaqueter ce qui est déjà compilé. La version n'est alors
    pas incrémentée : on republie un binaire existant, qui porte déjà la sienne.

.PARAMETER NoVersionBump
    Ne pas incrémenter la version avant de construire. Sans ce commutateur, le troisième champ
    d'AssemblyVersion et d'AssemblyFileVersion avance d'un cran à chaque publication.

.PARAMETER VersionsAConserver
    Nombre d'anciens setups à garder sur le partage. 0 (défaut) ne supprime rien.

.EXAMPLE
    .\deploy_update.ps1
    Construit, empaquette et publie.

.EXAMPLE
    .\deploy_update.ps1 -SkipBuild -VersionsAConserver 3
    Publie la compilation existante et ne garde que les trois derniers setups sur le partage.
#>

[CmdletBinding()]
param(
    [string]$ConfigPath,
    [string]$SolutionPath,
    [string]$ProjectName,
    [string]$UpdateFolder,
    [string]$Configuration = 'Release',
    [string]$Platform = 'Any CPU',
    [string]$CertificateFile,
    [string]$ChangelogUrl,
    [string]$InstallerArgs = '/SILENT /SP-',
    [bool]$Mandatory = $true,
    [switch]$SkipBuild,
    [switch]$NoVersionBump,
    [int]$VersionsAConserver = 0
)

$ErrorActionPreference = 'Stop'

# ---------------------------------------------------------------------------------------------
# Outils
# ---------------------------------------------------------------------------------------------

function Resolve-ExpressionInno {
    <#
        Évalue une expression ISPP simple : des littéraux entre guillemets et des noms déjà définis,
        assemblés par « + ».

        C'est ce qui permet à un fichier de configuration de ne nommer l'application qu'une fois et
        d'en dériver le reste. Sans cela, chaque valeur répète le nom et une copie d'un projet à
        l'autre en oublie toujours une.

        Renvoie $null sur une expression qu'on ne sait pas évaluer : mieux vaut ignorer la propriété
        que d'en deviner la valeur.
    #>
    param(
        [Parameter(Mandatory = $true)][string]$Expression,
        [Parameter(Mandatory = $true)][hashtable]$DejaDefinies
    )

    $morceaux = New-Object System.Collections.Generic.List[string]
    $courant = ''
    $dansGuillemets = $false

    foreach ($caractere in $Expression.ToCharArray()) {
        if ($caractere -eq '"') {
            $dansGuillemets = -not $dansGuillemets
            $courant += $caractere
            continue
        }
        # Un « + » entre guillemets appartient au texte, pas à l'expression.
        if ($caractere -eq '+' -and -not $dansGuillemets) {
            $morceaux.Add($courant)
            $courant = ''
            continue
        }
        $courant += $caractere
    }
    $morceaux.Add($courant)

    $resultat = ''
    foreach ($morceau in $morceaux) {
        $terme = $morceau.Trim()
        if ($terme -match '^"(.*)"$') { $resultat += $Matches[1]; continue }
        if ($DejaDefinies.ContainsKey($terme)) { $resultat += $DejaDefinies[$terme]; continue }
        return $null
    }

    return $resultat
}

function Get-ProprietesInno {
    <#
        Lit les #define du fichier de configuration Inno. C'est ce qui permet au .iss et à ce script
        de partager une seule source de vérité, au lieu de répéter les mêmes chemins des deux côtés.

        Les définitions sont lues dans l'ordre du fichier : une valeur peut donc s'appuyer sur celles
        déclarées au-dessus d'elle, exactement comme le fait le préprocesseur d'Inno.
    #>
    param([Parameter(Mandatory = $true)][string]$Chemin)

    $proprietes = @{}

    foreach ($ligne in [System.IO.File]::ReadAllLines($Chemin)) {
        $texte = $ligne.Trim()
        if ($texte.StartsWith(';')) { continue }

        $trouve = [regex]::Match($texte, '^#define\s+(\w+)\s+(.+?)\s*$')
        if (-not $trouve.Success) { continue }

        $valeur = Resolve-ExpressionInno -Expression $trouve.Groups[2].Value -DejaDefinies $proprietes
        if ($null -ne $valeur) { $proprietes[$trouve.Groups[1].Value] = $valeur }
    }

    return $proprietes
}

function Get-CheminIscc {
    <#
        Le compilateur Inno Setup. On interroge la base de registre avant de tomber sur les chemins
        habituels : une installation dans un dossier inhabituel reste ainsi trouvable.
    #>

    if ($env:ISCC -and (Test-Path $env:ISCC)) { return $env:ISCC }

    $cles = @(
        'HKLM:\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\Inno Setup 6_is1',
        'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\Inno Setup 6_is1'
    )
    foreach ($cle in $cles) {
        try {
            $dossier = (Get-ItemProperty -Path $cle -Name 'InstallLocation' -ErrorAction Stop).InstallLocation
            $candidat = Join-Path $dossier 'ISCC.exe'
            if (Test-Path $candidat) { return $candidat }
        }
        catch { }
    }

    $habituels = @(
        (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe'),
        (Join-Path $env:ProgramFiles 'Inno Setup 6\ISCC.exe')
    )
    foreach ($candidat in $habituels) {
        if (Test-Path $candidat) { return $candidat }
    }

    throw "ISCC.exe est introuvable. Installez Inno Setup 6 (https://jrsoftware.org/isdl.php), ou renseignez la variable d'environnement ISCC."
}

function Get-CheminSigntool {
    <#
        signtool n'est pas installé à un endroit stable : il arrive par le SDK Windows ou, comme
        souvent sur un poste de développement, par le paquet NuGet Microsoft.Windows.SDK.BuildTools.
        On retient la version la plus récente trouvée.
    #>

    $racines = @(
        (Join-Path ${env:ProgramFiles(x86)} 'Windows Kits\10\bin'),
        (Join-Path $env:USERPROFILE '.nuget\packages\microsoft.windows.sdk.buildtools')
    )

    foreach ($racine in $racines) {
        if (-not (Test-Path $racine)) { continue }

        $trouve = Get-ChildItem -Path $racine -Recurse -Filter 'signtool.exe' -ErrorAction SilentlyContinue |
            Where-Object { $_.FullName -match '\\x64\\' } |
            Sort-Object FullName -Descending |
            Select-Object -First 1

        if ($trouve) { return $trouve.FullName }
    }

    return $null
}

function Get-CheminMSBuild {
    <#
        vswhere.exe est installé à un emplacement fixe par tous les Visual Studio depuis 2017, quelle
        que soit l'édition et quelle que soit la version. C'est le seul repère qui survit à un
        passage de 2019 à 2022 puis au suivant : jamais de chemin de Visual Studio codé en dur.
    #>

    $vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'

    if (Test-Path $vswhere) {
        $trouve = & $vswhere -latest -prerelease -products '*' `
            -requires 'Microsoft.Component.MSBuild' `
            -find 'MSBuild\**\Bin\MSBuild.exe' 2>$null

        $chemin = $trouve | Where-Object { $_ } | Select-Object -First 1
        if ($chemin -and (Test-Path $chemin)) { return $chemin }
    }

    $commande = Get-Command 'msbuild.exe' -ErrorAction SilentlyContinue
    if ($commande) { return $commande.Source }

    throw "MSBuild est introuvable. Installez la charge de travail « Développement .NET desktop » de Visual Studio, ou lancez ce script depuis une invite de commandes développeur."
}

function Update-VersionAssembly {
    <#
        Incrémente AssemblyVersion et AssemblyFileVersion dans AssemblyInfo.cs, et renvoie la
        nouvelle version.

        Sans cet incrément rien ne bouge : AutoUpdater compare la version de l'assembly installé à
        celle annoncée dans update.xml, et le script Inno lit celle du binaire. Une version figée, et
        chaque poste se voit proposer indéfiniment la même mise à jour — ou aucune.

        Les deux attributs avancent ensemble : l'un est comparé par AutoUpdater, l'autre est lu par
        Inno dans les ressources de l'exécutable. Les laisser diverger ferait annoncer au setup une
        version différente de celle que le poste croit installer.

        Substitution dans le texte plutôt que réécriture du fichier : seuls les chiffres bougent, le
        reste est conservé octet pour octet.
    #>
    param([Parameter(Mandatory = $true)][string]$Chemin)

    $octets = [System.IO.File]::ReadAllBytes($Chemin)
    $avecBom = ($octets.Length -ge 3 -and $octets[0] -eq 0xEF -and $octets[1] -eq 0xBB -and $octets[2] -eq 0xBF)
    $contenu = [System.IO.File]::ReadAllText($Chemin)

    $motif = [regex]'\[assembly:\s*Assembly(?:File)?Version\("(\d+\.\d+\.\d+\.\d+)"\)\]'
    $correspondances = @($motif.Matches($contenu))
    if ($correspondances.Count -eq 0) {
        throw "Aucun attribut AssemblyVersion trouvé dans $Chemin."
    }

    $actuelle = [System.Version]$correspondances[0].Groups[1].Value
    $nouvelle = New-Object System.Version($actuelle.Major, $actuelle.Minor, ($actuelle.Build + 1), 0)

    # À rebours : remplacer depuis la fin garde valides les positions des occurrences précédentes.
    for ($i = $correspondances.Count - 1; $i -ge 0; $i--) {
        $champ = $correspondances[$i].Groups[1]
        $contenu = $contenu.Remove($champ.Index, $champ.Length).Insert($champ.Index, $nouvelle.ToString())
    }

    [System.IO.File]::WriteAllText($Chemin, $contenu, (New-Object System.Text.UTF8Encoding($avecBom)))
    return $nouvelle
}

function Find-Solution {
    <#
        Cherche la solution en remontant depuis le projet : construire un .csproj isolé échoue dès
        que ses références déclarent d'autres plateformes que lui, et c'est le rôle de la solution
        que de les faire correspondre.
    #>
    param([Parameter(Mandatory = $true)][string]$DossierDepart)

    $dossier = $DossierDepart
    while ($dossier) {
        $solutions = @(Get-ChildItem -Path (Join-Path $dossier '*') -File -Include '*.slnx', '*.sln' -ErrorAction SilentlyContinue)

        if ($solutions.Count -ge 1) {
            $prefere = $solutions | Where-Object { $_.Extension -eq '.slnx' } | Select-Object -First 1
            if (-not $prefere) { $prefere = $solutions[0] }
            return $prefere.FullName
        }

        $parent = Split-Path -Parent $dossier
        if ($parent -eq $dossier) { break }
        $dossier = $parent
    }
    return $null
}

function ConvertTo-CibleSolution {
    <#
        Dans une solution, chaque projet expose une cible portant son nom, où MSBuild remplace par
        « _ » les caractères qu'un nom de cible n'accepte pas.
    #>
    param([Parameter(Mandatory = $true)][string]$NomProjet)

    $cible = $NomProjet
    foreach ($caractere in @('%', '$', '@', ';', '.', '(', ')', "'")) {
        $cible = $cible.Replace($caractere, '_')
    }
    return $cible
}

function ConvertTo-UriFichier {
    <#
        AutoUpdater passe l'adresse à System.Uri : une URI file:// absolue ne laisse aucune place à
        l'interprétation, là où un chemin UNC brut dépend de la façon dont la chaîne est analysée.
    #>
    param([Parameter(Mandatory = $true)][string]$Chemin)

    return ([uri]$Chemin).AbsoluteUri
}

# ---------------------------------------------------------------------------------------------

try {
    # -----------------------------------------------------------------------------------------
    # 1. Configuration
    # -----------------------------------------------------------------------------------------

    if (-not $ConfigPath) { $ConfigPath = Join-Path $PSScriptRoot 'deploy.config.iss' }
    if (-not (Test-Path -LiteralPath $ConfigPath)) {
        throw "Configuration introuvable : $ConfigPath. Copiez deploy.config.example.iss en deploy.config.iss et renseignez-la."
    }

    $ConfigPath = (Resolve-Path -LiteralPath $ConfigPath).Path
    $dossierScript = Split-Path -Parent $ConfigPath
    $config = Get-ProprietesInno -Chemin $ConfigPath

    foreach ($requis in @('AppName', 'ExeName', 'SourceDir')) {
        if (-not $config.ContainsKey($requis)) { throw "#define $requis manquant dans $ConfigPath." }
    }

    $nomApp = $config['AppName']
    $nomExe = $config['ExeName']

    # Nom technique, pour ce qui devient un chemin ou une ligne de commande. Le nom affiche peut
    # contenir des espaces sans que cela pose probleme nulle part.
    $slug = $config['AppSlug']
    if (-not $slug) { $slug = $nomApp }

    $dossierSource = $config['SourceDir']
    if (-not [System.IO.Path]::IsPathRooted($dossierSource)) {
        $dossierSource = Join-Path $dossierScript $dossierSource
    }

    if (-not $UpdateFolder) { $UpdateFolder = $config['UpdateFolder'] }
    if (-not $UpdateFolder) {
        throw "Dossier de publication inconnu : renseignez #define UpdateFolder dans $ConfigPath, ou passez -UpdateFolder."
    }
    $UpdateFolder = $UpdateFolder.TrimEnd('\')

    Write-Host "Application : $nomApp" -ForegroundColor Cyan
    Write-Host "Destination : $UpdateFolder" -ForegroundColor Cyan

    # -----------------------------------------------------------------------------------------
    # 2. Construction
    # -----------------------------------------------------------------------------------------

    if ($SkipBuild) {
        Write-Host "Construction: ignorée (-SkipBuild)." -ForegroundColor DarkGray
    }
    else {
        # Le dossier de compilation est bin\<Configuration> : on remonte jusqu'au projet.
        $dossierProjet = [System.IO.Path]::GetFullPath((Join-Path $dossierSource '..\..'))

        if (-not $SolutionPath) { $SolutionPath = Find-Solution -DossierDepart $dossierProjet }
        if (-not $SolutionPath) {
            throw "Aucune solution trouvée depuis $dossierProjet. Passez -SolutionPath."
        }

        if (-not $ProjectName) {
            $projets = @(Get-ChildItem -Path (Join-Path $dossierProjet '*') -File -Include '*.csproj', '*.vbproj' -ErrorAction SilentlyContinue)
            if ($projets.Count -ne 1) {
                throw "Projet indéterminé dans $dossierProjet. Passez -ProjectName."
            }
            $ProjectName = [System.IO.Path]::GetFileNameWithoutExtension($projets[0].FullName)
        }

        # Incrément avant construction, pour que le binaire porte d'emblée la nouvelle version : le
        # script Inno et update.xml la reliront tous deux dans l'exécutable produit.
        if (-not $NoVersionBump) {
            $assemblyInfo = Join-Path $dossierProjet 'Properties\AssemblyInfo.cs'
            if (Test-Path -LiteralPath $assemblyInfo) {
                $nouvelleVersion = Update-VersionAssembly -Chemin $assemblyInfo
                Write-Host "Version     : incrémentée à $nouvelleVersion" -ForegroundColor Yellow
            }
            else {
                Write-Host "Version     : AssemblyInfo.cs introuvable, aucun incrément." -ForegroundColor Yellow
            }
        }

        $msbuild = Get-CheminMSBuild
        Write-Host "MSBuild     : $msbuild" -ForegroundColor DarkGray
        Write-Host "Solution    : $SolutionPath" -ForegroundColor DarkGray
        Write-Host "Construction de $ProjectName en $Configuration|$Platform..." -ForegroundColor Yellow

        & $msbuild $SolutionPath '-restore' "-t:$(ConvertTo-CibleSolution -NomProjet $ProjectName)" `
            '-nologo' '-verbosity:minimal' "-p:Configuration=$Configuration" "-p:Platform=$Platform"

        if ($LASTEXITCODE -ne 0) {
            throw "La construction a échoué (code $LASTEXITCODE). La publication est abandonnée."
        }
    }

    # -----------------------------------------------------------------------------------------
    # 3. Version, lue dans le binaire qui sera livré
    # -----------------------------------------------------------------------------------------

    $cheminExe = Join-Path $dossierSource $nomExe
    if (-not (Test-Path -LiteralPath $cheminExe)) {
        throw "Exécutable introuvable : $cheminExe. Construisez le projet, ou corrigez SourceDir."
    }

    $infos = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($cheminExe)
    $version = [System.Version]::new($infos.FileMajorPart, $infos.FileMinorPart, $infos.FileBuildPart, $infos.FilePrivatePart)
    Write-Host "Version     : $version" -ForegroundColor Cyan

    # -----------------------------------------------------------------------------------------
    # 4. Compilation du programme d'installation
    # -----------------------------------------------------------------------------------------

    $iscc = Get-CheminIscc
    $cheminIss = Join-Path $dossierScript 'deploy.iss'
    if (-not (Test-Path -LiteralPath $cheminIss)) { throw "deploy.iss introuvable à côté de $ConfigPath." }

    Write-Host "Compilation du programme d'installation..." -ForegroundColor Yellow
    & $iscc $cheminIss '/Qp'
    if ($LASTEXITCODE -ne 0) {
        throw "ISCC a échoué (code $LASTEXITCODE)."
    }

    $setup = Join-Path $dossierScript "Output\$slug-setup.exe"
    if (-not (Test-Path -LiteralPath $setup)) { throw "Setup introuvable après compilation : $setup" }

    # Signature une fois le setup produit, et non par la directive SignTool d'Inno : celle-ci attend
    # une ligne de commande complète en paramètre, et les guillemets qui entourent le chemin de
    # l'outil et celui du certificat n'y survivent pas.
    #
    # Sans horodatage : la signature d'un certificat auto-signé ne vaut de toute façon que le temps
    # de sa validité, et exiger un serveur d'horodatage rendrait la publication dépendante d'un accès
    # Internet.
    if (-not $CertificateFile -and $config.ContainsKey('CertPfx')) {
        $CertificateFile = $config['CertPfx']
        if (-not [System.IO.Path]::IsPathRooted($CertificateFile)) {
            $CertificateFile = Join-Path $dossierScript $CertificateFile
        }
    }

    if ($CertificateFile -and (Test-Path -LiteralPath $CertificateFile)) {
        $signtool = Get-CheminSigntool
        if ($signtool) {
            & $signtool sign /f $CertificateFile /fd SHA256 $setup | Out-Null
            if ($LASTEXITCODE -ne 0) { throw "La signature du setup a échoué (code $LASTEXITCODE)." }
            Write-Host "Signature   : $CertificateFile" -ForegroundColor DarkGray
        }
        else {
            Write-Host "Signature   : ignorée, signtool introuvable." -ForegroundColor Yellow
        }
    }

    # -----------------------------------------------------------------------------------------
    # 5. Publication
    # -----------------------------------------------------------------------------------------

    if (-not (Test-Path -LiteralPath $UpdateFolder)) {
        New-Item -Path $UpdateFolder -ItemType Directory -Force | Out-Null
        Write-Host "Dossier $UpdateFolder créé." -ForegroundColor Yellow
    }

    # Le setup porte sa version dans son nom : un poste qui télécharge pendant qu'on republie ne
    # récupère pas un fichier à moitié réécrit.
    $nomSetupPublie = "$slug-$version-setup.exe"
    $setupPublie = Join-Path $UpdateFolder $nomSetupPublie
    Copy-Item -LiteralPath $setup -Destination $setupPublie -Force

    if (-not $ChangelogUrl) {
        $changelog = Join-Path $UpdateFolder 'changelog.html'
        if (Test-Path -LiteralPath $changelog) { $ChangelogUrl = ConvertTo-UriFichier $changelog }
    }

    # update.xml : le format attendu par AutoUpdater.NET, dont la racine est <item>.
    $lignes = New-Object System.Collections.Generic.List[string]
    $lignes.Add('<?xml version="1.0" encoding="utf-8"?>')
    $lignes.Add('<item>')
    $lignes.Add("  <version>$version</version>")
    $lignes.Add("  <url>$([System.Security.SecurityElement]::Escape((ConvertTo-UriFichier $setupPublie)))</url>")
    if ($ChangelogUrl) {
        $lignes.Add("  <changelog>$([System.Security.SecurityElement]::Escape($ChangelogUrl))</changelog>")
    }
    # L'application peut de toute facon imposer la mise a jour depuis son code, auquel cas cette
    # valeur n'est pas relue. On l'ecrit quand meme juste : un update.xml qui annoncerait l'inverse
    # de ce que fait l'application se lirait comme une contradiction.
    $lignes.Add("  <mandatory>$($Mandatory.ToString().ToLower())</mandatory>")
    if ($InstallerArgs) {
        $lignes.Add("  <args>$([System.Security.SecurityElement]::Escape($InstallerArgs))</args>")
    }
    $lignes.Add('</item>')

    $cheminUpdateXml = Join-Path $UpdateFolder 'update.xml'
    [System.IO.File]::WriteAllLines($cheminUpdateXml, $lignes, (New-Object System.Text.UTF8Encoding($false)))

    # -----------------------------------------------------------------------------------------
    # 6. Purge des anciens setups (désactivée par défaut)
    # -----------------------------------------------------------------------------------------

    if ($VersionsAConserver -gt 0) {
        $anciens = @(Get-ChildItem -Path $UpdateFolder -File -Filter "$slug-*-setup.exe" |
            Where-Object { $_.Name -ne $nomSetupPublie } |
            Sort-Object LastWriteTime -Descending |
            Select-Object -Skip ([Math]::Max(0, $VersionsAConserver - 1)))

        foreach ($ancien in $anciens) {
            Remove-Item -LiteralPath $ancien.FullName -Force
            Write-Host "Ancienne version retirée : $($ancien.Name)" -ForegroundColor DarkGray
        }
    }

    Write-Host ""
    Write-Host "Publication terminée : version $version disponible." -ForegroundColor Green
    Write-Host "  $cheminUpdateXml" -ForegroundColor Green
    exit 0
}
catch {
    # Code de sortie non nul : ne jamais laisser croire qu'une version est en ligne alors qu'elle
    # n'y est pas.
    Write-Host ""
    Write-Error "Publication interrompue : $($_.Exception.Message)"
    exit 1
}
