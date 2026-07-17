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
using System.Deployment.Application;
using System.Runtime.InteropServices;



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

            if (ApplicationDeployment.IsNetworkDeployed)
            {
                var ad = ApplicationDeployment.CurrentDeployment;

                // Détecte si c'est le tout premier démarrage après l'installation ou une mise à jour
                if (ad.IsFirstRun)
                {
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
                                fbd.ShowNewFolderButton = true;

                                if (fbd.ShowDialog() == DialogResult.OK)
                                {
                                    // 🔑 1. On récupère le chemin sélectionné (potentiellement "Z:\mon\dossier")
                                    string cheminSelectionne = fbd.SelectedPath;

                                    // 🔑 2. On le convertit en UNC s'il s'agit d'un lecteur réseau (ex: "\\serveur\partage\mon\dossier")
                                    path = ObtenirCheminUNC(cheminSelectionne);

                                    // On sauvegarde le chemin UNC dans le fichier texte
                                    File.WriteAllText(fichierConfig, path);

                                    MessageBox.Show(
                                        $"Configuration enregistrée avec succès !\n\nChemin réel sauvegardé :\n{path}",
                                        "Configuration réussie",
                                        MessageBoxButtons.OK,
                                        MessageBoxIcon.Information
                                    );
                                }
                                else
                                {
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

                    MessageBox.Show(
                        "JBTExport a été installé avec succès !",
                        "Installation terminée",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information
                    );

                    // On ferme proprement l'application immédiatement
                    Environment.Exit(0);
                }
            }

           

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
                string dossierExportCible = Path.Combine(path, currentProjetName, indiceOwnerName, dossierFinalName);

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
                    PdmObjectId ownerPremierParentId = TSH.Pdm.GetOwner(premierParentId);
                    indiceOwnerName = ownerPremierParentId.IsEmpty ? projectName : TSH.Pdm.GetName(ownerPremierParentId);
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
