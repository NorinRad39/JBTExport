using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;

namespace JBTExport
{
    /// <summary>
    /// Pose, au premier lancement, un raccourci vers l'application sur le bureau de l'utilisateur.
    /// </summary>
    /// <remarks>
    /// MSIX ne sait pas créer de raccourci sur le bureau : le manifeste n'a aucun élément pour cela,
    /// et une application empaquetée n'apparaît que dans le menu Démarrer. C'est donc à
    /// l'application, qui tourne en runFullTrust, de s'en charger.
    ///
    /// Le raccourci ne vise pas l'exécutable — son chemin sous WindowsApps porte le numéro de
    /// version et change donc à chaque mise à jour — mais l'entrée de l'application dans le shell,
    /// « shell:AppsFolder » suivi de son identifiant. C'est ce que produit le « Créer un raccourci »
    /// manuel, et il survit aux mises à jour.
    ///
    /// Ce fichier est volontairement autonome et dupliqué à l'identique dans JBT-PDFViewer : les deux
    /// applications n'ont aucune bibliothèque commune, et l'introduire pour trente lignes coûterait
    /// plus qu'elle ne rapporterait.
    /// </remarks>
    internal static class RaccourciBureau
    {
        // GetCurrentPackageFamilyName renvoie ce code hors paquet : il sert donc aussi à savoir si
        // l'application tourne empaquetée, sans dépendance supplémentaire.
        private const int APPMODEL_ERROR_NO_PACKAGE = 15700;
        private const int ERROR_INSUFFICIENT_BUFFER = 122;

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetCurrentPackageFamilyName(ref int longueur, StringBuilder nom);

        /// <summary>
        /// Crée le raccourci du bureau s'il n'existe pas et qu'il n'a jamais été posé.
        /// </summary>
        /// <param name="nomRaccourci">Nom affiché sous l'icône, sans extension.</param>
        /// <param name="dossierMarqueur">
        /// Dossier de configuration de l'application, où est mémorisé le fait qu'on l'a déjà posé.
        /// </param>
        /// <remarks>
        /// Le marqueur n'est pas un détail : sans lui, un utilisateur qui supprime volontairement le
        /// raccourci le verrait revenir à chaque lancement.
        /// </remarks>
        public static void CreerSiAbsent(string nomRaccourci, string dossierMarqueur)
        {
            try
            {
                string famille = LireFamilleDePaquet();
                if (famille == null) return;

                string marqueur = Path.Combine(dossierMarqueur, "raccourci_bureau.txt");
                if (File.Exists(marqueur)) return;

                string bureau = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
                string chemin = Path.Combine(bureau, nomRaccourci + ".lnk");

                if (!File.Exists(chemin))
                {
                    Ecrire(chemin, "shell:AppsFolder\\" + famille + "!App", nomRaccourci);
                }

                Directory.CreateDirectory(dossierMarqueur);
                File.WriteAllText(marqueur, DateTime.Now.ToString("o"));
            }
            catch (Exception ex)
            {
                // Un raccourci manquant ne doit jamais empêcher l'application de démarrer.
                Console.WriteLine($"[Raccourci bureau] Création impossible : {ex.Message}");
            }
        }

        /// <summary>
        /// Nom de famille du paquet MSIX courant, ou <c>null</c> si l'application n'est pas empaquetée.
        /// </summary>
        private static string LireFamilleDePaquet()
        {
            int longueur = 0;

            // Premier appel avec un tampon vide : l'API renvoie la taille nécessaire, ou signale
            // qu'il n'y a pas de paquet du tout — c'est le cas au débogage depuis Visual Studio.
            int resultat = GetCurrentPackageFamilyName(ref longueur, null);
            if (resultat == APPMODEL_ERROR_NO_PACKAGE) return null;
            if (resultat != ERROR_INSUFFICIENT_BUFFER) return null;

            StringBuilder tampon = new StringBuilder(longueur);
            resultat = GetCurrentPackageFamilyName(ref longueur, tampon);

            return resultat == 0 ? tampon.ToString() : null;
        }

        /// <summary>
        /// Écrit le .lnk via WScript.Shell, en liaison tardive pour ne pas ajouter d'interop COM au
        /// projet.
        /// </summary>
        private static void Ecrire(string chemin, string cible, string description)
        {
            Type typeShell = Type.GetTypeFromProgID("WScript.Shell");
            if (typeShell == null) return;

            object shell = null;
            object raccourci = null;

            try
            {
                shell = Activator.CreateInstance(typeShell);
                raccourci = typeShell.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod,
                    null, shell, new object[] { chemin });

                Type typeRaccourci = raccourci.GetType();

                // Aucune icône à préciser : le lien vers l'entrée du shell porte déjà celle du paquet.
                typeRaccourci.InvokeMember("TargetPath", BindingFlags.SetProperty,
                    null, raccourci, new object[] { cible });
                typeRaccourci.InvokeMember("Description", BindingFlags.SetProperty,
                    null, raccourci, new object[] { description });
                typeRaccourci.InvokeMember("Save", BindingFlags.InvokeMethod,
                    null, raccourci, null);
            }
            finally
            {
                if (raccourci != null) Marshal.ReleaseComObject(raccourci);
                if (shell != null) Marshal.ReleaseComObject(shell);
            }
        }
    }
}
