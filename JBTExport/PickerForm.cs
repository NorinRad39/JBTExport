using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;
using OutilsTs;
using TopSolid.Kernel.Automating;
using TSH = TopSolid.Kernel.Automating.TopSolidHost;

namespace JBTExport
{
    /// <summary>
    /// Fenêtre en deux étapes : choix d'une pièce dans l'arborescence PDM, puis dépôt du lien
    /// SharePoint du classeur Excel à associer. Utilisée quand JBTExport est lancé sans liasse/plan
    /// actif dans TopSolid.
    /// </summary>
    public partial class PickerForm : Form
    {
        public PdmObjectId SelectedPdmObjectId { get; private set; }
        public string UrlSharePoint { get; private set; }

        /// <summary>Associe chaque nœud de l'arbre à son objet PDM et à sa nature (dossier ou document).</summary>
        private sealed class PdmNodeInfo
        {
            public PdmObjectId Id;
            public bool EstDocument;
        }

        /// <summary>Dossier PDM auquel se limite l'arborescence : les repères de l'atelier vivent tous dessous.</summary>
        private const string NomDossierAtelier = "02-Atelier";

        private const string CleIconeDossier = "dossier";
        private const string CleIconeFichierGenerique = "fichier";

        /// <summary>
        /// Icônes par extension, construites à la demande : la clé est l'extension PDM du document
        /// (ex. "TopPrt"), l'icône vient de l'association Windows pour cette extension (le même
        /// mécanisme que l'Explorateur), pas d'une icône TopSolid récupérée via l'API PDM, qui ne
        /// l'expose pas.
        /// </summary>
        private ImageList images;

        public PickerForm()
        {
            InitializeComponent();
            images = ConstruireImageListIcones();
            treeView1.ImageList = images;
            ChargerRacine();
        }

        private void ChargerRacine()
        {
            PdmObjectId projet = PDM.GetCurrentProjectPdmObject();
            PdmObjectId racine = TrouverDossierAtelier(projet);

            if (racine.IsEmpty)
            {
                MessageBox.Show(
                    $"Dossier \"{NomDossierAtelier}\" introuvable dans le PDM du projet courant.",
                    "Erreur", MessageBoxButtons.OK, MessageBoxIcon.Error);
                DialogResult = DialogResult.Cancel;
                Close();
                return;
            }

            string nomRacine = TSH.Pdm.GetName(racine);

            TreeNode noeudRacine = new TreeNode(nomRacine)
            {
                ImageKey = CleIconeDossier,
                SelectedImageKey = CleIconeDossier,
                Tag = new PdmNodeInfo { Id = racine, EstDocument = false }
            };
            AjouterPlaceholder(noeudRacine);

            treeView1.Nodes.Add(noeudRacine);
            noeudRacine.Expand();
        }

        private static PdmObjectId TrouverDossierAtelier(PdmObjectId projetPdm)
        {
            try
            {
                List<PdmObjectId> resultats = TSH.Pdm.SearchFolderByName(projetPdm, NomDossierAtelier);
                return resultats != null && resultats.Count > 0 ? resultats[0] : default(PdmObjectId);
            }
            catch
            {
                return default(PdmObjectId);
            }
        }

        /// <summary>Nœud fictif ajouté à tout dossier pour lui donner une flèche d'expansion sans charger son contenu tout de suite.</summary>
        private static void AjouterPlaceholder(TreeNode noeudDossier)
        {
            noeudDossier.Nodes.Add(new TreeNode("..."));
        }

        private void treeView1_BeforeExpand(object sender, TreeViewCancelEventArgs e)
        {
            TreeNode noeud = e.Node;

            bool estPlaceholder = noeud.Nodes.Count == 1 && noeud.Nodes[0].Tag == null;
            if (!estPlaceholder) return;

            noeud.Nodes.Clear();

            var info = (PdmNodeInfo)noeud.Tag;
            List<PdmObjectId> sousDossiers;
            List<PdmObjectId> documents;
            TSH.Pdm.GetConstituents(info.Id, out sousDossiers, out documents);

            foreach (PdmObjectId dossierId in sousDossiers)
            {
                TreeNode enfant = new TreeNode(TSH.Pdm.GetName(dossierId))
                {
                    ImageKey = CleIconeDossier,
                    SelectedImageKey = CleIconeDossier,
                    Tag = new PdmNodeInfo { Id = dossierId, EstDocument = false }
                };
                AjouterPlaceholder(enfant);
                noeud.Nodes.Add(enfant);
            }

            foreach (PdmObjectId documentId in documents)
            {
                string extension = ObtenirExtension(documentId);
                string nomAffiche = string.IsNullOrEmpty(extension)
                    ? TSH.Pdm.GetName(documentId)
                    : $"{TSH.Pdm.GetName(documentId)} (.{extension.TrimStart('.')})";
                string cleIcone = ObtenirCleIconeFichier(extension);

                TreeNode enfant = new TreeNode(nomAffiche)
                {
                    ImageKey = cleIcone,
                    SelectedImageKey = cleIcone,
                    Tag = new PdmNodeInfo { Id = documentId, EstDocument = true }
                };
                noeud.Nodes.Add(enfant);
            }
        }

        private static string ObtenirExtension(PdmObjectId documentId)
        {
            try
            {
                TSH.Pdm.GetType(documentId, out string extension);
                return extension;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Construit un ImageList de départ avec les icônes génériques "dossier" et "fichier" ;
        /// les icônes par extension (celle associée par Windows à l'extension du document, comme
        /// dans l'Explorateur) sont ajoutées à la volée par <see cref="ObtenirCleIconeFichier"/> au
        /// fil du chargement de l'arbre.
        /// </summary>
        private static ImageList ConstruireImageListIcones()
        {
            var images = new ImageList { ImageSize = new Size(16, 16), ColorDepth = ColorDepth.Depth32Bit };
            images.Images.Add(CleIconeDossier, ObtenirIcone("dossier", FILE_ATTRIBUTE_DIRECTORY));
            images.Images.Add(CleIconeFichierGenerique, ObtenirIcone("fichier", FILE_ATTRIBUTE_NORMAL));
            return images;
        }

        /// <summary>
        /// Renvoie la clé d'icône à utiliser pour un document de cette extension, en l'ajoutant à
        /// l'ImageList au premier rencontre. L'extension ne correspond pas forcément à un type
        /// enregistré dans Windows (les extensions PDM internes de TopSolid n'y figurent pas
        /// toutes) ; dans ce cas Windows renvoie déjà lui-même l'icône générique "fichier".
        /// </summary>
        private string ObtenirCleIconeFichier(string extension)
        {
            if (string.IsNullOrWhiteSpace(extension)) return CleIconeFichierGenerique;

            string cle = extension.TrimStart('.');
            if (!images.Images.ContainsKey(cle))
            {
                images.Images.Add(cle, ObtenirIcone("fichier." + cle, FILE_ATTRIBUTE_NORMAL));
            }

            return cle;
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr SHGetFileInfo(string pszPath, uint dwFileAttributes, ref SHFILEINFO psfi, uint cbSizeFileInfo, uint uFlags);

        [DllImport("user32.dll")]
        private static extern bool DestroyIcon(IntPtr hIcon);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct SHFILEINFO
        {
            public IntPtr hIcon;
            public int iIcon;
            public uint dwAttributes;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
            public string szDisplayName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]
            public string szTypeName;
        }

        private const uint SHGFI_ICON = 0x100;
        private const uint SHGFI_SMALLICON = 0x1;
        private const uint SHGFI_USEFILEATTRIBUTES = 0x10;
        private const uint FILE_ATTRIBUTE_DIRECTORY = 0x10;
        private const uint FILE_ATTRIBUTE_NORMAL = 0x80;

        /// <summary>
        /// Icône Windows associée au chemin fictif donné (résolue via son extension, comme le fait
        /// l'Explorateur) : avec SHGFI_USEFILEATTRIBUTES, Windows ne lit rien sur le disque, il se
        /// base uniquement sur l'extension/les attributs pour choisir l'icône.
        /// </summary>
        private static Bitmap ObtenirIcone(string cheminFictif, uint attributs)
        {
            var info = new SHFILEINFO();

            IntPtr resultat = SHGetFileInfo(
                cheminFictif, attributs, ref info, (uint)Marshal.SizeOf(info),
                SHGFI_ICON | SHGFI_SMALLICON | SHGFI_USEFILEATTRIBUTES);

            if (resultat == IntPtr.Zero || info.hIcon == IntPtr.Zero)
            {
                return SystemIcons.Application.ToBitmap();
            }

            try
            {
                using (Icon icone = Icon.FromHandle(info.hIcon))
                {
                    return icone.ToBitmap();
                }
            }
            finally
            {
                DestroyIcon(info.hIcon);
            }
        }

        private void treeView1_AfterSelect(object sender, TreeViewEventArgs e)
        {
            var info = e.Node.Tag as PdmNodeInfo;
            btnOk.Enabled = info != null && info.EstDocument;
        }

        private void btnOk_Click(object sender, EventArgs e)
        {
            if (treeView1.SelectedNode == null) return;

            var info = (PdmNodeInfo)treeView1.SelectedNode.Tag;
            SelectedPdmObjectId = info.Id;

            panelArbre.Visible = false;
            panelLien.Visible = true;
            txtLien.Focus();
        }

        private void btnAnnulerArbre_Click(object sender, EventArgs e)
        {
            DialogResult = DialogResult.Cancel;
            Close();
        }

        private void panelDropZone_DragEnter(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.Text) || e.Data.GetDataPresent("UniformResourceLocator"))
            {
                e.Effect = DragDropEffects.Copy;
                lblErreurLien.Visible = false;
            }
            else if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                // Cas déjà rencontré par l'utilisateur : glisser le fichier Excel local au lieu du
                // lien web ne fonctionne pas, on le signale plutôt que de rejeter sans explication.
                e.Effect = DragDropEffects.None;
                lblErreurLien.Text = "Dépose le lien web du fichier (depuis le navigateur ou Teams), pas le fichier lui-même.";
                lblErreurLien.Visible = true;
            }
            else
            {
                e.Effect = DragDropEffects.None;
            }
        }

        private void panelDropZone_DragDrop(object sender, DragEventArgs e)
        {
            string texte = e.Data.GetDataPresent(DataFormats.Text)
                ? (string)e.Data.GetData(DataFormats.Text)
                : LireFormatUrl(e.Data);

            txtLien.Text = texte?.Trim();
            ValiderEtFermer();
        }

        /// <summary>Lit le format de glisser-déposer "UniformResourceLocator" utilisé par certains navigateurs.</summary>
        private static string LireFormatUrl(IDataObject data)
        {
            if (!data.GetDataPresent("UniformResourceLocator")) return null;

            object brut = data.GetData("UniformResourceLocator");

            if (brut is MemoryStream flux)
            {
                using (var lecteur = new StreamReader(flux, Encoding.UTF8))
                {
                    return lecteur.ReadToEnd().TrimEnd('\0');
                }
            }

            return brut as string;
        }

        private void btnValiderLien_Click(object sender, EventArgs e)
        {
            ValiderEtFermer();
        }

        private void ValiderEtFermer()
        {
            string texte = txtLien.Text?.Trim();

            Uri url;
            bool valide = !string.IsNullOrEmpty(texte)
                && Uri.TryCreate(texte, UriKind.Absolute, out url)
                && (url.Scheme == Uri.UriSchemeHttp || url.Scheme == Uri.UriSchemeHttps);

            if (!valide)
            {
                lblErreurLien.Text = "Lien invalide : dépose ou colle une adresse http(s) valide.";
                lblErreurLien.Visible = true;
                return;
            }

            UrlSharePoint = texte;
            DialogResult = DialogResult.OK;
            Close();
        }

        private void btnRetourLien_Click(object sender, EventArgs e)
        {
            panelLien.Visible = false;
            panelArbre.Visible = true;
        }

        private void btnAnnulerLien_Click(object sender, EventArgs e)
        {
            DialogResult = DialogResult.Cancel;
            Close();
        }
    }
}
