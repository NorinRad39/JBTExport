<#
.SYNOPSIS
    Publie un projet de packaging MSIX (.wapproj) et met le paquet à disposition sur un partage
    réseau au moyen d'un fichier .appinstaller.

.DESCRIPTION
    Deux usages, qui déploient l'un comme l'autre exactement une fois :

      - Depuis Visual Studio (assistant de publication, ou build Release) : le projet appelle ce
        script avec -SkipBuild. Le script se contente alors de publier les artefacts que MSBuild
        vient de produire.

      - En ligne de commande, sans rien d'autre : le script localise MSBuild, construit le paquet,
        puis le publie. C'est le mode par défaut, celui d'un simple  .\deploy_update.ps1

    Réutilisation sur un autre projet : déposer le script à côté du .wapproj. Tout le reste est
    déduit du projet — nom du paquet, plateformes du bundle, dossier de déploiement (lu dans
    AppInstallerUri). Aucun chemin n'est codé en dur ici.

    Le fichier .appinstaller est réécrit intégralement à chaque publication, à partir de l'identité
    lue dans le paquet lui-même. C'est volontaire : la version annoncée aux postes clients doit être
    exactement celle du paquet livré, sans quoi Windows refuse la mise à jour.

.PARAMETER ProjectPath
    Le .wapproj à publier. Par défaut : l'unique .wapproj situé à côté du script.

.PARAMETER SolutionPath
    La solution par laquelle construire. Par défaut : la première trouvée en remontant depuis le
    projet. Passer par la solution n'est pas un détail — c'est elle qui fait correspondre la
    plateforme du projet de packaging et celle de l'application empaquetée.

.PARAMETER DeployFolder
    Le dossier de destination, en général un partage réseau. Par défaut : le dossier désigné par la
    propriété AppInstallerUri du projet.

.PARAMETER Configuration
    Configuration MSBuild. Release par défaut.

.PARAMETER Platform
    Plateforme MSBuild du projet de packaging. Par défaut : la première de AppxBundlePlatforms.
    Ne détermine pas le contenu du bundle, qui reste piloté par BundlePlatforms.

.PARAMETER BundlePlatforms
    Plateformes embarquées dans le bundle, séparées par « | ». Par défaut : AppxBundlePlatforms du
    projet.

.PARAMETER AppInstallerName
    Nom du fichier .appinstaller publié. Par défaut : le nom du projet.

.PARAMETER SkipBuild
    Ne rien construire, publier les artefacts existants. C'est le mode utilisé quand MSBuild appelle
    le script en fin de build.

.PARAMETER NoVersionBump
    Ne pas incrémenter la version du manifeste avant de construire. Sans ce commutateur, le script
    incrémente comme le ferait l'assistant, dès lors que le projet a AppxAutoIncrementPackageRevision
    à True. N'a aucun effet avec -SkipBuild : c'est alors Visual Studio qui a déjà incrémenté.

.PARAMETER VersionsAConserver
    Nombre d'anciennes versions à garder sur le partage. 0 (défaut) ne supprime rien.

.EXAMPLE
    .\deploy_update.ps1
    Construit puis publie, avec les valeurs déduites du projet.

.EXAMPLE
    .\deploy_update.ps1 -DeployFolder '\\serveur\partage$\MonApp' -VersionsAConserver 3
    Publie ailleurs et ne conserve que les trois dernières versions sur le partage.
#>

[CmdletBinding()]
param(
    [string]$ProjectPath,
    [string]$SolutionPath,
    [string]$DeployFolder,
    [string]$Configuration = 'Release',
    [string]$Platform,
    [string]$BundlePlatforms,
    [string]$AppInstallerName,
    [switch]$SkipBuild,
    [switch]$NoVersionBump,
    [int]$VersionsAConserver = 0
)

$ErrorActionPreference = 'Stop'

# ---------------------------------------------------------------------------------------------
# Outils
# ---------------------------------------------------------------------------------------------

function Get-ProprieteProjet {
    <#
        Lit une propriété MSBuild dans le XML du projet, sans se soucier de l'espace de noms : les
        .wapproj utilisent l'ancien (developer/msbuild/2003), les projets SDK n'en ont aucun.
    #>
    param(
        [Parameter(Mandatory = $true)][xml]$Projet,
        [Parameter(Mandatory = $true)][string]$Nom
    )

    $noeud = $Projet.SelectSingleNode("//*[local-name()='PropertyGroup']/*[local-name()='$Nom']")
    if ($noeud) { return $noeud.InnerText.Trim() }
    return $null
}

function ConvertTo-CheminLocal {
    <#
        AppInstallerUri se présente selon les cas comme une URI file://, comme un chemin UNC, ou
        comme un chemin UNC dont Visual Studio a échappé le $ des partages administratifs. Les trois
        doivent ramener au même dossier.
    #>
    param([string]$Valeur)

    if ([string]::IsNullOrWhiteSpace($Valeur)) { return $null }

    $decode = [uri]::UnescapeDataString($Valeur.Trim())
    if ($decode -match '^[a-zA-Z][a-zA-Z0-9+.-]*://') { return ([uri]$decode).LocalPath }
    return $decode
}

function Get-CheminMSBuild {
    <#
        vswhere.exe est installé à un emplacement fixe par tous les Visual Studio depuis 2017, quelle
        que soit l'édition et quelle que soit la version. C'est le seul repère qui survit à un
        passage de 2019 à 2022 puis à 18 : jamais de chemin de Visual Studio codé en dur.
    #>

    $vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'

    if (Test-Path $vswhere) {
        $trouve = & $vswhere -latest -prerelease -products '*' `
            -requires 'Microsoft.Component.MSBuild' `
            -find 'MSBuild\**\Bin\MSBuild.exe' 2>$null

        $chemin = $trouve | Where-Object { $_ } | Select-Object -First 1
        if ($chemin -and (Test-Path $chemin)) { return $chemin }
    }

    # Repli : invite de commandes développeur, ou MSBuild présent dans le PATH.
    $commande = Get-Command 'msbuild.exe' -ErrorAction SilentlyContinue
    if ($commande) { return $commande.Source }

    throw "MSBuild est introuvable. Installez la charge de travail « Développement .NET desktop » de Visual Studio, ou lancez ce script depuis une invite de commandes développeur."
}

function Get-IdentitePaquet {
    <#
        Lit Name / Version / Publisher dans le manifeste embarqué du paquet.

        On ouvre le paquet plutôt que d'analyser son nom de fichier : la version annoncée dans le
        .appinstaller doit être celle que Windows lira réellement à l'installation. Une version
        déduite du nom, ou pire incrémentée à l'aveugle, fait échouer la mise à jour côté client.
    #>
    param([Parameter(Mandatory = $true)][string]$Chemin)

    Add-Type -AssemblyName System.IO.Compression.FileSystem | Out-Null

    $archive = [System.IO.Compression.ZipFile]::OpenRead($Chemin)
    try {
        $entree = $archive.GetEntry('AppxMetadata/AppxBundleManifest.xml')
        if (-not $entree) { $entree = $archive.GetEntry('AppxManifest.xml') }
        if (-not $entree) { throw "Aucun manifeste trouvé dans $Chemin." }

        $flux = $entree.Open()
        try {
            $lecteur = New-Object System.IO.StreamReader($flux)
            [xml]$manifeste = $lecteur.ReadToEnd()
        }
        finally { $flux.Dispose() }
    }
    finally { $archive.Dispose() }

    $identite = $manifeste.SelectSingleNode("/*/*[local-name()='Identity']")
    if (-not $identite) { throw "Identité absente du manifeste de $Chemin." }

    return [pscustomobject]@{
        Nom     = $identite.GetAttribute('Name')
        Version = $identite.GetAttribute('Version')
        Editeur = $identite.GetAttribute('Publisher')
    }
}

function Get-VersionDepuisNom {
    param([string]$Nom)

    if ($Nom -match '_(\d+\.\d+\.\d+\.\d+)_') { return [System.Version]$Matches[1] }
    return [System.Version]'0.0.0.0'
}

function Update-VersionManifeste {
    <#
        Incrémente le troisième champ de Identity/@Version dans le manifeste, comme le fait la case
        « Incrémenter automatiquement » de l'assistant.

        Indispensable en ligne de commande : AppxAutoIncrementPackageRevision est une action de
        l'IDE, MSBuild ne l'applique pas. Sans cet incrément, une publication en ligne de commande
        republierait indéfiniment la même version, et aucun poste ne verrait jamais de mise à jour.
    #>
    param([Parameter(Mandatory = $true)][string]$CheminManifeste)

    # Substitution dans le texte, et non via XmlDocument : même avec PreserveWhitespace, Save()
    # recompose l'intérieur des balises et remet le manifeste entier sur une seule ligne par
    # élément. Le fichier apparaîtrait intégralement modifié dans l'historique pour un seul chiffre.
    # Ici, seuls les chiffres de la version bougent, le reste est conservé octet pour octet.
    $octets = [System.IO.File]::ReadAllBytes($CheminManifeste)
    $avecBom = ($octets.Length -ge 3 -and $octets[0] -eq 0xEF -and $octets[1] -eq 0xBB -and $octets[2] -eq 0xBF)
    $contenu = [System.IO.File]::ReadAllText($CheminManifeste)

    # [^>] ne franchit pas la fin de balise : la recherche reste donc dans l'élément Identity, et ne
    # peut pas attraper un MinVersion ou un MaxVersionTested situé plus loin.
    $correspondance = [regex]::Match($contenu, '<Identity\b[^>]*?\bVersion\s*=\s*"(\d+\.\d+\.\d+\.\d+)"')
    if (-not $correspondance.Success) {
        throw "Version d'identité introuvable dans $CheminManifeste."
    }

    $champ = $correspondance.Groups[1]
    $actuelle = [System.Version]$champ.Value
    $nouvelle = New-Object System.Version($actuelle.Major, $actuelle.Minor, ($actuelle.Build + 1), 0)

    $contenu = $contenu.Remove($champ.Index, $champ.Length).Insert($champ.Index, $nouvelle.ToString())
    [System.IO.File]::WriteAllText($CheminManifeste, $contenu, (New-Object System.Text.UTF8Encoding($avecBom)))

    return $nouvelle
}

function Find-Solution {
    <#
        Cherche la solution en remontant depuis le projet.

        Construire le .wapproj directement échoue dès que l'application empaquetée ne déclare pas la
        même plateforme que lui : MSBuild propage Platform=x86 à un projet qui ne connaît qu'AnyCPU,
        et s'arrête sur « BaseOutputPath/OutputPath n'est pas définie ». C'est précisément le rôle
        de la solution que de faire correspondre les plateformes, et c'est ce que fait Visual Studio.
    #>
    param([Parameter(Mandatory = $true)][string]$DossierDepart)

    $dossier = $DossierDepart
    while ($dossier) {
        $solutions = @(Get-ChildItem -Path (Join-Path $dossier '*') -File -Include '*.slnx', '*.sln' -ErrorAction SilentlyContinue)

        if ($solutions.Count -ge 1) {
            # .slnx d'abord : quand les deux coexistent, c'est le format que Visual Studio ouvre.
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

# ---------------------------------------------------------------------------------------------

try {
    # -----------------------------------------------------------------------------------------
    # 1. Projet et paramètres déduits
    # -----------------------------------------------------------------------------------------

    if (-not $ProjectPath) {
        $projets = @(Get-ChildItem -Path $PSScriptRoot -Filter '*.wapproj' -File)
        if ($projets.Count -eq 0) {
            throw "Aucun .wapproj à côté du script ($PSScriptRoot). Passez -ProjectPath."
        }
        if ($projets.Count -gt 1) {
            throw "Plusieurs .wapproj à côté du script. Précisez lequel avec -ProjectPath."
        }
        $ProjectPath = $projets[0].FullName
    }

    $ProjectPath = (Resolve-Path -LiteralPath $ProjectPath).Path
    $dossierProjet = Split-Path -Parent $ProjectPath
    $nomProjet = [System.IO.Path]::GetFileNameWithoutExtension($ProjectPath)

    Write-Host "Projet      : $nomProjet" -ForegroundColor Cyan

    [xml]$projetXml = Get-Content -LiteralPath $ProjectPath -Raw

    if (-not $BundlePlatforms) {
        $BundlePlatforms = Get-ProprieteProjet -Projet $projetXml -Nom 'AppxBundlePlatforms'
    }
    if (-not $Platform) {
        if ($BundlePlatforms) { $Platform = ($BundlePlatforms -split '\|')[0].Trim() }
        else { $Platform = 'x64' }
    }
    if (-not $AppInstallerName) {
        # Le nom doit rester stable : c'est l'adresse que les postes déjà installés interrogent pour
        # se mettre à jour. En changer les couperait de toute mise à jour ultérieure.
        $AppInstallerName = "$nomProjet.appinstaller"
    }
    if (-not $DeployFolder) {
        $uriProjet = Get-ProprieteProjet -Projet $projetXml -Nom 'AppInstallerUri'
        $chemin = ConvertTo-CheminLocal -Valeur $uriProjet
        if ($chemin) {
            # AppInstallerUri désigne tantôt le dossier, tantôt le fichier lui-même.
            if ([System.IO.Path]::GetExtension($chemin) -eq '.appinstaller') {
                $chemin = Split-Path -Parent $chemin
            }
            $DeployFolder = $chemin.TrimEnd('\')
        }
    }
    if (-not $DeployFolder) {
        throw "Dossier de déploiement inconnu : renseignez AppInstallerUri dans $nomProjet, ou passez -DeployFolder."
    }

    $heuresEntreVerifications = Get-ProprieteProjet -Projet $projetXml -Nom 'HoursBetweenUpdateChecks'
    if (-not $heuresEntreVerifications) { $heuresEntreVerifications = '0' }

    $dossierPaquets = Get-ProprieteProjet -Projet $projetXml -Nom 'AppxPackageDir'
    if (-not $dossierPaquets) { $dossierPaquets = 'AppPackages' }
    if (-not [System.IO.Path]::IsPathRooted($dossierPaquets)) {
        $dossierPaquets = Join-Path $dossierProjet $dossierPaquets
    }

    Write-Host "Destination : $DeployFolder" -ForegroundColor Cyan

    # -----------------------------------------------------------------------------------------
    # 2. Construction du paquet
    # -----------------------------------------------------------------------------------------

    if ($SkipBuild) {
        Write-Host "Construction: ignorée (-SkipBuild), publication des artefacts existants." -ForegroundColor DarkGray
    }
    else {
        # Incrément avant construction, pour que le paquet porte d'emblée la nouvelle version. On
        # suit le réglage du projet : si l'assistant n'incrémente pas, le script non plus.
        $incrementDemande = (Get-ProprieteProjet -Projet $projetXml -Nom 'AppxAutoIncrementPackageRevision')
        if (-not $NoVersionBump -and $incrementDemande -and $incrementDemande -match '^(true|True)$') {
            $noeudManifeste = $projetXml.SelectSingleNode("//*[local-name()='AppxManifest']")
            $nomManifeste = if ($noeudManifeste) { $noeudManifeste.GetAttribute('Include') } else { 'Package.appxmanifest' }
            $cheminManifeste = Join-Path $dossierProjet $nomManifeste

            if (Test-Path -LiteralPath $cheminManifeste) {
                $nouvelleVersion = Update-VersionManifeste -CheminManifeste $cheminManifeste
                Write-Host "Version     : incrémentée à $nouvelleVersion" -ForegroundColor Yellow
            }
        }

        $msbuild = Get-CheminMSBuild
        Write-Host "MSBuild     : $msbuild" -ForegroundColor DarkGray

        if (-not $SolutionPath) { $SolutionPath = Find-Solution -DossierDepart $dossierProjet }

        if ($SolutionPath) {
            Write-Host "Solution    : $SolutionPath" -ForegroundColor DarkGray
            $aConstruire = $SolutionPath
            $cible = "-t:$(ConvertTo-CibleSolution -NomProjet $nomProjet)"
        }
        else {
            # Sans solution, on tente le projet seul. Cela ne fonctionne que si l'application
            # empaquetée déclare la plateforme demandée : sinon, passer -SolutionPath.
            $aConstruire = $ProjectPath
            $cible = '-t:Build'
        }

        Write-Host "Construction de $Configuration|$Platform (bundle $BundlePlatforms)..." -ForegroundColor Yellow

        $arguments = @(
            $aConstruire
            '-restore'
            $cible
            '-nologo'
            '-verbosity:minimal'
            "-p:Configuration=$Configuration"
            "-p:Platform=$Platform"
            '-p:UapAppxPackageBuildMode=SideloadOnly'
            '-p:AppxPackageSigningEnabled=true'
            '-p:GenerateAppInstallerFile=true'
            # Le projet rappelle ce script en fin de build : sans ce garde-fou, publier en ligne de
            # commande enchaînerait deux déploiements pour un seul paquet.
            '-p:SkipDeploy=true'
        )
        if ($BundlePlatforms) { $arguments += "-p:AppxBundlePlatforms=$BundlePlatforms" }

        & $msbuild @arguments
        if ($LASTEXITCODE -ne 0) {
            throw "La construction a échoué (code $LASTEXITCODE). Le déploiement est abandonné."
        }
    }

    # -----------------------------------------------------------------------------------------
    # 3. Paquet à publier
    # -----------------------------------------------------------------------------------------

    if (-not (Test-Path -LiteralPath $dossierPaquets)) {
        throw "Dossier des paquets introuvable : $dossierPaquets"
    }

    $paquet = Get-ChildItem -Path $dossierPaquets -Recurse -File -Include '*.msixbundle', '*.appxbundle', '*.msix', '*.appx' |
        Sort-Object LastWriteTime -Descending |
        Select-Object -First 1

    if (-not $paquet) {
        throw "Aucun paquet (.msixbundle, .msix) trouvé sous $dossierPaquets"
    }

    $identite = Get-IdentitePaquet -Chemin $paquet.FullName
    Write-Host "Paquet      : $($paquet.Name)" -ForegroundColor Cyan
    Write-Host "Version     : $($identite.Version)" -ForegroundColor Cyan

    # -----------------------------------------------------------------------------------------
    # 4. Copie sur le partage
    # -----------------------------------------------------------------------------------------

    if (-not (Test-Path -LiteralPath $DeployFolder)) {
        New-Item -Path $DeployFolder -ItemType Directory -Force | Out-Null
        Write-Host "Dossier $DeployFolder créé." -ForegroundColor Yellow
    }

    $paquetPublie = Join-Path $DeployFolder $paquet.Name
    Copy-Item -LiteralPath $paquet.FullName -Destination $paquetPublie -Force

    # Le certificat de signature accompagne le paquet : un poste qui ne connaît pas encore l'éditeur
    # en a besoin pour la première installation.
    $certificat = Get-ChildItem -Path $paquet.DirectoryName -Filter '*.cer' -File -ErrorAction SilentlyContinue |
        Select-Object -First 1
    if ($certificat) {
        Copy-Item -LiteralPath $certificat.FullName -Destination (Join-Path $DeployFolder $certificat.Name) -Force
    }

    # -----------------------------------------------------------------------------------------
    # 5. Fichier .appinstaller
    # -----------------------------------------------------------------------------------------

    $cheminAppInstaller = Join-Path $DeployFolder $AppInstallerName

    # Réécriture complète plutôt que retouche de celui produit par Visual Studio : ses URI pointent
    # vers l'arborescence locale AppPackages, où les postes clients n'iront jamais chercher.
    if ($paquet.Extension -in '.msixbundle', '.appxbundle') { $balise = 'MainBundle' }
    else { $balise = 'MainPackage' }

    $uriAppInstaller = ([uri]$cheminAppInstaller).AbsoluteUri
    $uriPaquet = ([uri]$paquetPublie).AbsoluteUri

    $contenu = @"
<?xml version="1.0" encoding="utf-8"?>
<AppInstaller
    xmlns="http://schemas.microsoft.com/appx/appinstaller/2017/2"
    Uri="$([System.Security.SecurityElement]::Escape($uriAppInstaller))"
    Version="$([System.Security.SecurityElement]::Escape($identite.Version))">
  <$balise
    Name="$([System.Security.SecurityElement]::Escape($identite.Nom))"
    Version="$([System.Security.SecurityElement]::Escape($identite.Version))"
    Publisher="$([System.Security.SecurityElement]::Escape($identite.Editeur))"
    Uri="$([System.Security.SecurityElement]::Escape($uriPaquet))" />
  <UpdateSettings>
    <OnLaunch HoursBetweenUpdateChecks="$([System.Security.SecurityElement]::Escape($heuresEntreVerifications))" />
  </UpdateSettings>
</AppInstaller>
"@

    # UTF-8 sans BOM, écrit explicitement : Set-Content -Encoding UTF8 en ajoute un sous Windows
    # PowerShell 5.1, qui est l'hôte utilisé quand MSBuild appelle ce script.
    [System.IO.File]::WriteAllText($cheminAppInstaller, $contenu, (New-Object System.Text.UTF8Encoding($false)))

    # -----------------------------------------------------------------------------------------
    # 6. Purge des anciennes versions (désactivée par défaut)
    # -----------------------------------------------------------------------------------------

    if ($VersionsAConserver -gt 0) {
        $anciens = @(Get-ChildItem -Path $DeployFolder -File -Filter "*$($paquet.Extension)" |
            Where-Object { $_.Name -ne $paquet.Name } |
            Sort-Object { Get-VersionDepuisNom $_.Name } -Descending |
            Select-Object -Skip ([Math]::Max(0, $VersionsAConserver - 1)))

        foreach ($ancien in $anciens) {
            Remove-Item -LiteralPath $ancien.FullName -Force

            # Le certificat porte le même nom que le paquet : le laisser derrière encombrerait le
            # partage de fichiers qui ne servent plus à rien.
            $certAncien = Join-Path $DeployFolder ([System.IO.Path]::GetFileNameWithoutExtension($ancien.Name) + '.cer')
            if (Test-Path -LiteralPath $certAncien) { Remove-Item -LiteralPath $certAncien -Force }

            Write-Host "Ancienne version retirée : $($ancien.Name)" -ForegroundColor DarkGray
        }
    }

    Write-Host ""
    Write-Host "Publication terminée : version $($identite.Version) disponible." -ForegroundColor Green
    Write-Host "  $cheminAppInstaller" -ForegroundColor Green
    exit 0
}
catch {
    # Code de sortie non nul : MSBuild doit faire échouer la publication plutôt que laisser croire
    # que la nouvelle version est en ligne.
    Write-Host ""
    Write-Error "Déploiement interrompu : $($_.Exception.Message)"
    exit 1
}
