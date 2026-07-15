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

            FindExporterIndexByExtension("Pdf", out int exporterIndex);
            Console.WriteLine($"Exporter index for PDF: {exporterIndex}");
            List<KeyValue> options = TSH.Application.GetExporterOptions(exporterIndex);
            foreach (KeyValue kv in options)
            {
                Console.WriteLine($"Option: {kv.Key} = {kv.Value}");
            }
        }

            private static bool FindExporterIndexByExtension(string extension, out int exporterIndex)
            {
                exporterIndex = -1;

                for (int i = 0; i < TSH.Application.ExporterCount; i++)
                {
                    TSH.Application.GetExporterFileType(i, out string fileTypeName, out string[] outFileExtensions);

                    // Vérifier si l'extension existe dans le tableau
                    if (outFileExtensions != null && outFileExtensions.Any(ext => ext.TrimStart('.').Equals(extension, StringComparison.OrdinalIgnoreCase)))
                    {
                        exporterIndex = i;
                        return true;
                    }
                }

                return false;
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