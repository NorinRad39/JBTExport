using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TopSolid.Kernel.Automating;
using TSH = TopSolid.Kernel.Automating.TopSolidHost;
using OutilsTs;
using System.IO;
using System.Windows.Forms;
using System.Diagnostics;
using System.Net;
using System.Net.Cache;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Xml.Serialization;
using AutoUpdaterDotNET;



namespace JBTExport
{
    internal class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            string dossierConfig = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "JBTExport");
            string fichierConfig = Path.Combine(dossierConfig, "config.txt");
            string path = string.Empty;

            // Filet de sécurité : le programme d'installation pose déjà ce raccourci, sous le même
            // nom. L'appel ne sert donc que pour un poste rattrapé autrement — copie de dossier,
            // script de session — et ne fait rien quand le setup est passé avant.
            Deploiement.RaccourciBureau.CreerSiAbsent("JBTExport", dossierConfig);

            // Contrôle de version avant tout : un poste en retard ne doit pas exporter avec un
            // format que les autres ne relisent pas.
            if (!PeutDemarrer()) return;

            try
            {
                // 1. On s'assure que le dossier de config existe
                if (!Directory.Exists(dossierConfig))
                {
                    Directory.CreateDirectory(dossierConfig);
                }

                // 2. Si le fichier existe déjà, on tente de lire le chemin
                if (File.Exists(fichierConfig))
                {
                    path = File.ReadAllText(fichierConfig).Trim();
                }

                // 3. Si le chemin est vide ou n'existe pas physiquement sur le disque
                if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
                {
                    MessageBox.Show(
                        "Aucun dossier d'exportation valide n'est configuré.\n\nVeuillez sélectionner le dossier d'exportation par défaut dans la fenêtre qui va suivre.",
                        "Configuration du chemin d'export",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information
                    );

                    // 🔑 Utilisation de FolderBrowserDialog pour ouvrir l'explorateur Windows
                    using (var fbd = new FolderBrowserDialog())
                    {
                        fbd.Description = "Sélectionnez le dossier d'exportation par défaut pour JBT-Export";
                        fbd.ShowNewFolderButton = true; // Permet de créer un dossier à la volée

                        if (fbd.ShowDialog() == DialogResult.OK)
                        {
                            path = fbd.SelectedPath;

                            // On sauvegarde directement le choix propre de l'utilisateur dans le fichier texte
                            File.WriteAllText(fichierConfig, path);

                            MessageBox.Show(
                                $"Configuration enregistrée avec succès !\n\nChemin sauvegardé :\n{path}",
                                "Configuration réussie",
                                MessageBoxButtons.OK,
                                MessageBoxIcon.Information
                            );
                        }
                        else
                        {
                            // Si l'utilisateur clique sur "Annuler", on ne peut pas continuer
                            MessageBox.Show(
                                "L'exportation a été annulée car aucun dossier n'a été sélectionné.",
                                "Export annulé",
                                MessageBoxButtons.OK,
                                MessageBoxIcon.Warning
                            );
                            return;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Erreur lors de la configuration du chemin : {ex.Message}", "Erreur", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            // Sécurité pour s'assurer que le chemin d'export se termine bien par un '\'
            if (!path.EndsWith("\\"))
            {
                path += "\\";
            }

            Console.WriteLine($"Le chemin d'exportation utilisé est : {path}");

            var connector = new StartConnect();
            connector.ConnectionTopsolid();

            if(TSH.IsConnected)
            {
                Console.WriteLine("Connecté à TopSolid.");
            }
            else
            {
                MessageBox.Show("Échec de la connexion à TopSolid.", "Erreur", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            var currentDoc = new Document();

            if(currentDoc == null || currentDoc.DocId == DocumentId.Empty)
            {
                MessageBox.Show("Aucun document ouvert dans TopSolid.", "Erreur", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            var currentProjetName = PDM.GetCurrentProjectName();
            string indiceOwnerName = string.Empty;
            string dossierFinalName = TrouverIndiceRecurssif(currentDoc.DocPdmObject, currentProjetName, out indiceOwnerName);

            try
            {
                // 1. On calcule le chemin du dossier cible final
                string dossierExportCible = string.IsNullOrWhiteSpace(indiceOwnerName)
                    ? Path.Combine(path, currentProjetName, dossierFinalName)
                    : Path.Combine(path, currentProjetName, indiceOwnerName, dossierFinalName);

                // 2. On s'assure que le dossier de destination existe physiquement
                if (!Directory.Exists(dossierExportCible))
                {
                    Directory.CreateDirectory(dossierExportCible);
                    Console.WriteLine($"Dossier créé : {dossierExportCible}");
                }

                // 3. On s'assure que le chemin se termine par un anti-slash pour la méthode d'export
                string cheminExportFinal = dossierExportCible + "\\";

                // 4. On lance l'exportation (génère directement ton .jbt)
                Export.ExportDocId(currentDoc.DocId, cheminExportFinal, currentDoc.DocNomTxt, ".jbt");

                // ==========================================
                // 🔑 5. AJOUT DE LA GÉNÉRATION DES SIGNETS DIRECTEMENT SUR LE .JBT
                // ==========================================
                string fichierJbtExistant = Path.Combine(dossierExportCible, currentDoc.DocNomTxt + ".jbt");

                if (File.Exists(fichierJbtExistant))
                {
                    // On envoie le fichier .jbt directement à notre traitement
                    Signets.CreerSignetsNavigables(fichierJbtExistant, currentDoc.DocNomTxt);
                }
                else
                {
                    Console.WriteLine($"[Signets] Impossible de trouver le fichier exporté : {fichierJbtExistant}");
                }
                // ==========================================

                MessageBox.Show("Export terminé avec succès.", "Succès", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (ArgumentException ex)
            {
                MessageBox.Show($"Paramètre invalide pour l'export : {ex.Message}", "Erreur", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            catch (InvalidOperationException ex)
            {
                MessageBox.Show($"État invalide pour l'export : {ex.Message}", "Erreur", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Erreur inattendue pendant l'export : {ex.Message}", "Erreur", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }

            TSH.Disconnect();
            Application.Exit();

        }

        /// <summary>Adresse du descripteur de mise à jour, sur le partage réseau.</summary>
        /// <remarks>
        /// Chemin UNC et non une lettre de lecteur : un mappage est propre à la session, et sur un
        /// poste où il manque les mises à jour cesseraient sans que personne ne s'en aperçoive.
        /// </remarks>
        private const string DescripteurMiseAJour =
            @"\\jbtec-be\meca$\topsolid\JBTExport\update.xml";

        /// <summary>
        /// Indique si l'application est autorisée à démarrer, après contrôle de sa version.
        /// </summary>
        /// <remarks>
        /// La mise à jour peut être refusée, mais l'application ne démarre pas tant qu'elle n'est
        /// pas faite : tous les postes doivent produire des liasses au même format.
        ///
        /// Le contrôle est fait ici, avant toute fenêtre, et non par AutoUpdater.Start : celui-ci
        /// mène le déroulé de bout en bout et ne dit pas si l'utilisateur a refusé. Ses boîtes de
        /// dialogue s'affichent très bien sans boucle de messages, elles pompent la leur.
        /// </remarks>
        private static bool PeutDemarrer()
        {
            UpdateInfoEventArgs descripteur;

            try
            {
                descripteur = LireDescripteurMiseAJour();
            }
            catch (Exception ex)
            {
                // Partage injoignable, poste hors réseau : on laisse travailler plutôt que
                // d'immobiliser. Ne pas savoir n'est pas la même chose que savoir qu'une version
                // manque.
                Console.WriteLine($"[Mise à jour] Vérification impossible : {ex.Message}");
                return true;
            }

            Version installee = Assembly.GetExecutingAssembly().GetName().Version;
            if (descripteur == null || string.IsNullOrWhiteSpace(descripteur.CurrentVersion)) return true;
            if (new Version(descripteur.CurrentVersion) <= installee) return true;

            DialogResult reponse = MessageBox.Show(
                $"La version {descripteur.CurrentVersion} est disponible ; ce poste utilise la {installee}.\n\n"
                + "JBTExport ne peut pas s'ouvrir tant que la mise à jour n'est pas faite.",
                "Mise à jour requise",
                MessageBoxButtons.OKCancel,
                MessageBoxIcon.Information);

            if (reponse != DialogResult.OK)
            {
                Console.WriteLine("[Mise à jour] Refusée : l'application ne démarre pas.");
                return false;
            }

            // L'installation se fait par utilisateur, dans %LOCALAPPDATA% : aucune élévation n'est
            // nécessaire. Sans ce réglage, AutoUpdater lance le programme d'installation avec le
            // verbe « runas » et déclenche une invite UAC pour rien.
            AutoUpdater.RunUpdateAsAdmin = false;

            // Rend la main une fois le programme d'installation lancé : celui-ci remplace les
            // fichiers puis relance l'application.
            if (AutoUpdater.DownloadUpdate(descripteur)) return false;

            MessageBox.Show(
                "Le téléchargement de la mise à jour n'a pas abouti.\n\n"
                + "Vérifiez l'accès au réseau, puis relancez JBTExport.",
                "Mise à jour",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);

            return false;
        }

        /// <summary>
        /// Lit le descripteur publié sur le partage.
        /// </summary>
        private static UpdateInfoEventArgs LireDescripteurMiseAJour()
        {
            using (WebClient client = new WebClient())
            {
                // Sans cela, un update.xml fraîchement publié peut rester masqué par le cache.
                client.CachePolicy = new RequestCachePolicy(RequestCacheLevel.NoCacheNoStore);

                string xml = client.DownloadString(new Uri(DescripteurMiseAJour));

                XmlSerializer serialiseur = new XmlSerializer(typeof(UpdateInfoEventArgs));
                using (StringReader lecteur = new StringReader(xml))
                {
                    return (UpdateInfoEventArgs)serialiseur.Deserialize(lecteur);
                }
            }
        }

        private static string TrouverIndiceRecurssif(PdmObjectId elementId, string projectName, out string indiceOwnerName, PdmObjectId premierParentId = default(PdmObjectId), string premierParentName = null)
        {
            indiceOwnerName = string.Empty;

            if (elementId.IsEmpty) return string.Empty;

            PdmObjectId parentId = TSH.Pdm.GetOwner(elementId);
            if (parentId.IsEmpty) return string.Empty;

            string parentName = TSH.Pdm.GetName(parentId);

            if (premierParentId.IsEmpty)
            {
                premierParentId = parentId;
                premierParentName = parentName;
            }

            // Condition d'arrêt 1 : On a trouvé un dossier Ind
            if (parentName.StartsWith("Ind", StringComparison.OrdinalIgnoreCase))
            {
                var indiceOwner = TSH.Pdm.GetOwner(parentId);
                indiceOwnerName = indiceOwner.IsEmpty ? projectName : TSH.Pdm.GetName(indiceOwner);
                return parentName;
            }

            // Condition d'arrêt 2 : On est remonté jusqu'au projet sans trouver de dossier Ind
            if (string.Equals(parentName, projectName, StringComparison.OrdinalIgnoreCase))
            {
                if (!premierParentId.IsEmpty)
                {
                    // Pas de dossier Ind trouvé => on n'ajoute pas de niveau intermédiaire
                    indiceOwnerName = string.Empty;
                    return premierParentName ?? string.Empty;
                }

                return string.Empty;
            }

            // Appel récursif : On relance la recherche sur le parent
            return TrouverIndiceRecurssif(parentId, projectName, out indiceOwnerName, premierParentId, premierParentName);
        }

        // Import de l'API Windows pour résoudre les chemins réseau
        [DllImport("mpr.dll", CharSet = CharSet.Unicode)]
        [return: MarshalAs(UnmanagedType.U4)]
        private static extern int WNetGetConnection(
            [MarshalAs(UnmanagedType.LPWStr)] string localName,
            [MarshalAs(UnmanagedType.LPWStr)] StringBuilder remoteName,
            [In, Out] ref int length);

        /// <summary>
        /// Convertit un chemin local (avec lettre de lecteur) en chemin UNC si c'est un lecteur réseau.
        /// </summary>
        public static string ObtenirCheminUNC(string cheminOriginal)
        {
            if (string.IsNullOrWhiteSpace(cheminOriginal) || cheminOriginal.Length < 2 || cheminOriginal[1] != ':')
            {
                return cheminOriginal;
            }

            // On extrait la lettre du lecteur (ex: "Z:")
            string lettreLecteur = cheminOriginal.Substring(0, 2);
            int tailleBuffer = 512;
            StringBuilder buffer = new StringBuilder(tailleBuffer);

            // Appelle l'API Windows pour récupérer le chemin réseau derrière la lettre
            int resultat = WNetGetConnection(lettreLecteur, buffer, ref tailleBuffer);

            if (resultat == 0) // 0 = Success
            {
                // On remplace "Z:" par "\\Serveur\Partage" et on recolle le reste du chemin
                string resteDuChemin = cheminOriginal.Substring(2);
                return buffer.ToString().TrimEnd() + resteDuChemin;
            }

            // Si ce n'est pas un lecteur réseau (ex: disque C: local), on retourne le chemin d'origine
            return cheminOriginal;
        }
    }
}
