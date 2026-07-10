using OutilsTs;
using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using TopSolid.Kernel.Automating;
using TSH = TopSolid.Kernel.Automating.TopSolidHost;

namespace JBPrint
{
    internal class Program
    {
        // ─── API WINDOWS ───
        private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool IsWindowVisible(IntPtr hWnd);

        private static IntPtr _fenetreTrouvee = IntPtr.Zero;

        [DllImport("user32.dll")]
        private static extern IntPtr GetDlgItem(IntPtr hDlg, int nIDDlgItem);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern IntPtr SendMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

        // Constantes Windows standard
        private const int IDYES = 6;       // ID universel du bouton "Oui" / "Yes"
        private const uint BM_CLICK = 0xF5; // Message Windows pour simuler un clic de souris

        static void Main(string[] args)
        {
            var connector = new StartConnect();
            connector.ConnectionTopsolid();

            var document = new OutilsTs.Document();

            if (!TSH.IsConnected) return;

            if (document.DocId == null)
            {
                Console.WriteLine("Aucun document ouvert.");
                return;
            }

            if (document.DocExtention == ".TopDft" || document.DocExtention == ".TopDftBdl")
            {
                int resolutionPrinted = 700;
                PrintColorMapping typeCouleur = PrintColorMapping.Color;
                TSH.Application.CurrentPrinterName = "Microsoft Print to PDF";

                string dossierSortie = @"C:\Users\norin\Desktop\Nouveau dossier\";
                string cheminCompletPdf = Path.Combine(dossierSortie, document.DocNomTxt + ".jbt");

                if (!Directory.Exists(dossierSortie)) Directory.CreateDirectory(dossierSortie);

                // 🆕 1. On lance le robot dans un Thread STA dédié pour le Presse-papiers
                Thread robotThread = new Thread(() => RobotEnregistrementEclair(cheminCompletPdf));
                robotThread.SetApartmentState(ApartmentState.STA);
                robotThread.Start();

                // 2. On déclenche l'impression TopSolid
                Console.WriteLine("Impression lancée...");
                TSH.Documents.Print(document.DocId, typeCouleur, resolutionPrinted);

                Console.WriteLine("Traitement terminé.");
            }
            Console.ReadLine();
        }

        private static void RobotEnregistrementEclair(string cheminAEnregistrer)
        {
            int tentative = 0;
            _fenetreTrouvee = IntPtr.Zero;

            // 1. On cherche la fenêtre principale d'enregistrement
            while (_fenetreTrouvee == IntPtr.Zero && tentative < 50)
            {
                EnumWindows(EvaluerFenetre, IntPtr.Zero);
                if (_fenetreTrouvee == IntPtr.Zero)
                {
                    Thread.Sleep(10);
                    tentative++;
                }
            }

            if (_fenetreTrouvee != IntPtr.Zero)
            {
                IntPtr fenetrePrincipale = _fenetreTrouvee;

                // Focus et envoi du chemin initial
                SetForegroundWindow(fenetrePrincipale);
                Thread.Sleep(50);
                Clipboard.SetText(cheminAEnregistrer);
                SendKeys.SendWait("^v{ENTER}");

                // 2. GESTION DE L'ALERTE D'ÉCRASEMENT
                IntPtr fenetreAlerte = IntPtr.Zero;
                for (int i = 0; i < 50; i++)
                {
                    Thread.Sleep(10);
                    fenetreAlerte = TrouverSousFenetreAlerte(fenetrePrincipale);
                    if (fenetreAlerte != IntPtr.Zero) break;
                }

                // Si la fenêtre d'alerte est apparue (le fichier existait déjà)
                if (fenetreAlerte != IntPtr.Zero)
                {
                    // 🆕 On récupère directement le handle du bouton "Oui" (ID 6)
                    IntPtr handleBtnOui = GetDlgItem(fenetreAlerte, IDYES);

                    if (handleBtnOui != IntPtr.Zero)
                    {
                        // On envoie le signal de clic directement au bouton.
                        // Cela fonctionne instantanément, peu importe où est le focus.
                        SendMessage(handleBtnOui, BM_CLICK, IntPtr.Zero, IntPtr.Zero);
                        Console.WriteLine("✓ Clic explicite sur le bouton 'Oui' effectué via l'API Windows.");
                    }
                    else
                    {
                        // Fallback de sécurité au cas où l'ID standard échouerait sur une version future de Windows
                        // On force le focus et on envoie le raccourci clavier Alt+O (pour 'Oui' en français)
                        SetForegroundWindow(fenetreAlerte);
                        Thread.Sleep(50);
                        SendKeys.SendWait("%o"); // % représente la touche ALT en SendKeys
                        Console.WriteLine("✓ Remplacement validé via le raccourci Alt+O.");
                    }
                }
            }
        }

        // 🆕 Variable et méthode pour intercepter la boîte d'alerte du même processus
        private static IntPtr _sousFenetreTrouvee = IntPtr.Zero;

        private static IntPtr TrouverSousFenetreAlerte(IntPtr fenetrePrincipale)
        {
            _sousFenetreTrouvee = IntPtr.Zero;

            EnumWindows((hWnd, lParam) =>
            {
                // On cherche une AUTRE fenêtre visible, de classe #32770, liée au même PID
                if (hWnd != fenetrePrincipale && IsWindowVisible(hWnd))
                {
                    StringBuilder strClasse = new StringBuilder(256);
                    GetClassName(hWnd, strClasse, strClasse.Capacity);

                    if (strClasse.ToString() == "#32770")
                    {
                        GetWindowThreadProcessId(hWnd, out uint pidAlerte);
                        GetWindowThreadProcessId(fenetrePrincipale, out uint pidPrincipal);

                        if (pidAlerte == pidPrincipal)
                        {
                            _sousFenetreTrouvee = hWnd;
                            return false; // Fenêtre trouvée, on arrête le scan
                        }
                    }
                }
                return true;
            }, IntPtr.Zero);

            return _sousFenetreTrouvee;
        }

        private static bool EvaluerFenetre(IntPtr hWnd, IntPtr lParam)
        {
            if (!IsWindowVisible(hWnd)) return true;

            StringBuilder strClasse = new StringBuilder(256);
            GetClassName(hWnd, strClasse, strClasse.Capacity);

            if (strClasse.ToString() == "#32770")
            {
                GetWindowThreadProcessId(hWnd, out uint pid);
                try
                {
                    Process proc = Process.GetProcessById((int)pid);
                    string nomProcessus = proc.ProcessName.ToLower();

                    if (nomProcessus.Contains("topsolid") || nomProcessus.Contains(Process.GetCurrentProcess().ProcessName.ToLower()))
                    {
                        _fenetreTrouvee = hWnd;
                        return false;
                    }
                }
                catch { }
            }
            return true;
        }
    }
}