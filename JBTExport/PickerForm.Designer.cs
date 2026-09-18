namespace JBTExport
{
    partial class PickerForm
    {
        private System.ComponentModel.IContainer components = null;

        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        private System.Windows.Forms.Panel panelArbre;
        private System.Windows.Forms.Label lblInstructionArbre;
        private System.Windows.Forms.TreeView treeView1;
        private System.Windows.Forms.Panel panelBoutonsArbre;
        private System.Windows.Forms.Button btnOk;
        private System.Windows.Forms.Button btnAnnulerArbre;

        private System.Windows.Forms.Panel panelLien;
        private System.Windows.Forms.Label lblInstructionLien;
        private System.Windows.Forms.Panel panelDropZone;
        private System.Windows.Forms.Label lblDropZone;
        private System.Windows.Forms.TextBox txtLien;
        private System.Windows.Forms.Label lblErreurLien;
        private System.Windows.Forms.Panel panelBoutonsLien;
        private System.Windows.Forms.Button btnValiderLien;
        private System.Windows.Forms.Button btnRetourLien;
        private System.Windows.Forms.Button btnAnnulerLien;

        private void InitializeComponent()
        {
            this.panelArbre = new System.Windows.Forms.Panel();
            this.panelBoutonsArbre = new System.Windows.Forms.Panel();
            this.btnOk = new System.Windows.Forms.Button();
            this.btnAnnulerArbre = new System.Windows.Forms.Button();
            this.treeView1 = new System.Windows.Forms.TreeView();
            this.lblInstructionArbre = new System.Windows.Forms.Label();

            this.panelLien = new System.Windows.Forms.Panel();
            this.lblErreurLien = new System.Windows.Forms.Label();
            this.txtLien = new System.Windows.Forms.TextBox();
            this.panelDropZone = new System.Windows.Forms.Panel();
            this.lblDropZone = new System.Windows.Forms.Label();
            this.lblInstructionLien = new System.Windows.Forms.Label();
            this.panelBoutonsLien = new System.Windows.Forms.Panel();
            this.btnAnnulerLien = new System.Windows.Forms.Button();
            this.btnRetourLien = new System.Windows.Forms.Button();
            this.btnValiderLien = new System.Windows.Forms.Button();

            this.SuspendLayout();

            // ---- panelArbre ----
            this.panelArbre.Dock = System.Windows.Forms.DockStyle.Fill;
            this.panelArbre.Controls.Add(this.treeView1);
            this.panelArbre.Controls.Add(this.panelBoutonsArbre);
            this.panelArbre.Controls.Add(this.lblInstructionArbre);
            this.panelArbre.Name = "panelArbre";

            this.lblInstructionArbre.Dock = System.Windows.Forms.DockStyle.Top;
            this.lblInstructionArbre.Height = 44;
            this.lblInstructionArbre.Padding = new System.Windows.Forms.Padding(10);
            this.lblInstructionArbre.Text = "Sélectionne la pièce concernée dans le PDM (dossier \"3D\" du repère), puis clique sur OK.";
            this.lblInstructionArbre.Name = "lblInstructionArbre";

            this.treeView1.Dock = System.Windows.Forms.DockStyle.Fill;
            this.treeView1.Name = "treeView1";
            this.treeView1.BeforeExpand += new System.Windows.Forms.TreeViewCancelEventHandler(this.treeView1_BeforeExpand);
            this.treeView1.AfterSelect += new System.Windows.Forms.TreeViewEventHandler(this.treeView1_AfterSelect);

            this.panelBoutonsArbre.Dock = System.Windows.Forms.DockStyle.Bottom;
            this.panelBoutonsArbre.Height = 46;
            this.panelBoutonsArbre.Controls.Add(this.btnOk);
            this.panelBoutonsArbre.Controls.Add(this.btnAnnulerArbre);
            this.panelBoutonsArbre.Name = "panelBoutonsArbre";

            this.btnOk.Text = "OK";
            this.btnOk.Enabled = false;
            this.btnOk.Size = new System.Drawing.Size(90, 28);
            this.btnOk.Location = new System.Drawing.Point(410, 9);
            this.btnOk.Anchor = System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right;
            this.btnOk.Name = "btnOk";
            this.btnOk.Click += new System.EventHandler(this.btnOk_Click);

            this.btnAnnulerArbre.Text = "Annuler";
            this.btnAnnulerArbre.Size = new System.Drawing.Size(90, 28);
            this.btnAnnulerArbre.Location = new System.Drawing.Point(310, 9);
            this.btnAnnulerArbre.Anchor = System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right;
            this.btnAnnulerArbre.Name = "btnAnnulerArbre";
            this.btnAnnulerArbre.Click += new System.EventHandler(this.btnAnnulerArbre_Click);

            // ---- panelLien ----
            this.panelLien.Dock = System.Windows.Forms.DockStyle.Fill;
            this.panelLien.Visible = false;
            this.panelLien.Controls.Add(this.txtLien);
            this.panelLien.Controls.Add(this.lblErreurLien);
            this.panelLien.Controls.Add(this.panelBoutonsLien);
            this.panelLien.Controls.Add(this.panelDropZone);
            this.panelLien.Controls.Add(this.lblInstructionLien);
            this.panelLien.Name = "panelLien";

            this.lblInstructionLien.Dock = System.Windows.Forms.DockStyle.Top;
            this.lblInstructionLien.Height = 44;
            this.lblInstructionLien.Padding = new System.Windows.Forms.Padding(10);
            this.lblInstructionLien.Text = "Dépose le lien SharePoint du classeur Excel (depuis le navigateur ou Teams), ou colle-le ci-dessous.";
            this.lblInstructionLien.Name = "lblInstructionLien";

            this.panelDropZone.Dock = System.Windows.Forms.DockStyle.Top;
            this.panelDropZone.Height = 110;
            this.panelDropZone.Margin = new System.Windows.Forms.Padding(10);
            this.panelDropZone.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.panelDropZone.AllowDrop = true;
            this.panelDropZone.Controls.Add(this.lblDropZone);
            this.panelDropZone.Name = "panelDropZone";
            this.panelDropZone.DragEnter += new System.Windows.Forms.DragEventHandler(this.panelDropZone_DragEnter);
            this.panelDropZone.DragDrop += new System.Windows.Forms.DragEventHandler(this.panelDropZone_DragDrop);

            this.lblDropZone.Dock = System.Windows.Forms.DockStyle.Fill;
            this.lblDropZone.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
            this.lblDropZone.Text = "Dépose ici le lien SharePoint";
            this.lblDropZone.AllowDrop = true;
            this.lblDropZone.Name = "lblDropZone";
            this.lblDropZone.DragEnter += new System.Windows.Forms.DragEventHandler(this.panelDropZone_DragEnter);
            this.lblDropZone.DragDrop += new System.Windows.Forms.DragEventHandler(this.panelDropZone_DragDrop);

            this.txtLien.Location = new System.Drawing.Point(13, 168);
            this.txtLien.Size = new System.Drawing.Size(475, 22);
            this.txtLien.Anchor = System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left | System.Windows.Forms.AnchorStyles.Right;
            this.txtLien.Name = "txtLien";

            this.lblErreurLien.Location = new System.Drawing.Point(13, 196);
            this.lblErreurLien.Size = new System.Drawing.Size(475, 40);
            this.lblErreurLien.Anchor = System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left | System.Windows.Forms.AnchorStyles.Right;
            this.lblErreurLien.ForeColor = System.Drawing.Color.Firebrick;
            this.lblErreurLien.Visible = false;
            this.lblErreurLien.Name = "lblErreurLien";

            this.panelBoutonsLien.Dock = System.Windows.Forms.DockStyle.Bottom;
            this.panelBoutonsLien.Height = 46;
            this.panelBoutonsLien.Controls.Add(this.btnValiderLien);
            this.panelBoutonsLien.Controls.Add(this.btnRetourLien);
            this.panelBoutonsLien.Controls.Add(this.btnAnnulerLien);
            this.panelBoutonsLien.Name = "panelBoutonsLien";

            this.btnValiderLien.Text = "Créer le lien";
            this.btnValiderLien.Size = new System.Drawing.Size(110, 28);
            this.btnValiderLien.Location = new System.Drawing.Point(390, 9);
            this.btnValiderLien.Anchor = System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right;
            this.btnValiderLien.Name = "btnValiderLien";
            this.btnValiderLien.Click += new System.EventHandler(this.btnValiderLien_Click);

            this.btnRetourLien.Text = "< Retour";
            this.btnRetourLien.Size = new System.Drawing.Size(90, 28);
            this.btnRetourLien.Location = new System.Drawing.Point(13, 9);
            this.btnRetourLien.Anchor = System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left;
            this.btnRetourLien.Name = "btnRetourLien";
            this.btnRetourLien.Click += new System.EventHandler(this.btnRetourLien_Click);

            this.btnAnnulerLien.Text = "Annuler";
            this.btnAnnulerLien.Size = new System.Drawing.Size(90, 28);
            this.btnAnnulerLien.Location = new System.Drawing.Point(290, 9);
            this.btnAnnulerLien.Anchor = System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right;
            this.btnAnnulerLien.Name = "btnAnnulerLien";
            this.btnAnnulerLien.Click += new System.EventHandler(this.btnAnnulerLien_Click);

            // ---- PickerForm ----
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(520, 480);
            this.Controls.Add(this.panelLien);
            this.Controls.Add(this.panelArbre);
            this.MinimumSize = new System.Drawing.Size(420, 380);
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterParent;
            this.Text = "Créer un lien SharePoint";
            this.Name = "PickerForm";

            this.ResumeLayout(false);
        }
    }
}
