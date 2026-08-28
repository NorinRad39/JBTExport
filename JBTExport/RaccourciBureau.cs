using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;

namespace Deploiement
{
    /// <summary>
    /// Pose un raccourci vers l'application sur le bureau de l'utilisateur, s'il n'y est pas.
    /// </summary>
    /// <remarks>
    /// À n'utiliser que si le programme d'installation ne s'en charge pas déjà. Inno Setup pose le
    /// raccourci nativement, à l'installation, et le retire à la désinstallation — c'est mieux fait
    /// que depuis l'application. Cette classe reste utile quand l'application est déployée
    /// autrement : copie de dossier, script de session, ancien poste rattrapé à la main.
    ///
    /// La cible est le chemin de l'exécutable en cours, et non un chemin écrit en dur : c'est le bon
    /// chemin par construction, et il n'y a rien à tenir à jour si le dossier d'installation change.
    /// </remarks>
    internal static class RaccourciBureau
    {
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
                string executable = Assembly.GetEntryAssembly()?.Location;
                if (string.IsNullOrEmpty(executable) || !EstInstallee(executable)) return;

                string marqueur = Path.Combine(dossierMarqueur, "raccourci_bureau.txt");
                if (File.Exists(marqueur)) return;

                string bureau = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
                string chemin = Path.Combine(bureau, nomRaccourci + ".lnk");

                if (!File.Exists(chemin))
                {
                    Ecrire(chemin, executable, nomRaccourci);
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
        /// Vrai lorsque l'exécutable tourne depuis son dossier d'installation, et non depuis un
        /// dossier de compilation.
        /// </summary>
        /// <remarks>
        /// Sans ce garde-fou, chaque exécution depuis Visual Studio poserait sur le bureau un
        /// raccourci vers bin\Debug.
        /// </remarks>
        private static bool EstInstallee(string executable)
        {
            string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (string.IsNullOrEmpty(local)) return false;

            return Path.GetFullPath(executable)
                .StartsWith(Path.GetFullPath(local), StringComparison.OrdinalIgnoreCase);
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

                typeRaccourci.InvokeMember("TargetPath", BindingFlags.SetProperty,
                    null, raccourci, new object[] { cible });
                typeRaccourci.InvokeMember("WorkingDirectory", BindingFlags.SetProperty,
                    null, raccourci, new object[] { Path.GetDirectoryName(cible) });
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
