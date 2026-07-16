using iText.Kernel.Pdf;
using iText.Kernel.Pdf.Canvas.Parser;
using iText.Kernel.Pdf.Navigation;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace JBTExport
{
    internal class Signets
    {
        /// <summary>
        /// Ajoute un signet racine et des sous-signets pointant vers les pages d'un PDF.
        /// </summary>
        /// <param name="cheminPdf">Chemin complet du fichier PDF existant.</param>
        /// <param name="titreSignetPrincipal">Le titre du dossier/signet parent (ex: "Plan JBT").</param>
        public static void CreerSignetsNavigables(string cheminPdf, string titreSignetPrincipal)
        {
            if (!File.Exists(cheminPdf)) return;

            // Fichier temporaire pour manipuler le PDF sans verrouiller le fichier d'origine
            string tempFile = Path.GetTempFileName();
            bool hasChanges = false;

            try
            {
                using (PdfReader reader = new PdfReader(cheminPdf))
                using (PdfWriter writer = new PdfWriter(tempFile))
                using (PdfDocument pdfDoc = new PdfDocument(reader, writer))
                {
                    int totalPages = pdfDoc.GetNumberOfPages();
                    if (totalPages == 0) return;

                    // Initialise l'arbre des signets (Outlines)
                    // 1. Récupérer l'arbre des signets (renvoie un PdfOutline)
                    // Le paramètre "update" à 'false' indique qu'on veut juste lire les signets existants s'ils existent.
                    PdfOutline rootOutline = pdfDoc.GetOutlines(false);

                    // 2. Si le document n'a pas encore de signets (rootOutline est null)
                    if (rootOutline == null)
                    {
                        // On initialise les signets sur le document. 
                        // Attention : cette méthode renvoie 'void', donc on ne l'assigne à rien !
                        pdfDoc.InitializeOutlines();

                        // Ensuite, on récupère le rootOutline fraîchement initialisé
                        rootOutline = pdfDoc.GetOutlines(true);
                    }

                    // 1. Création du signet principal (Dossier parent) qui pointe vers la page 1
                    PdfOutline parentOutline = rootOutline.AddOutline(titreSignetPrincipal);
                    parentOutline.AddDestination(PdfExplicitDestination.CreateFit(pdfDoc.GetPage(1)));

                    // Dans ta boucle de détection dans Signets.cs :
                    for (int i = 1; i <= totalPages; i++)
                    {
                        var page = pdfDoc.GetPage(i);
                        var pageSize = page.GetPageSize();

                        // 🔑 On utilise la liste de l'Option A (ConfigSignets)
                        var detector = new Detector(pageSize, ConfigSignets.MotsClesCentraux);

                        var processor = new PdfCanvasProcessor(detector);
                        processor.ProcessPageContent(page);

                        if (detector.FoundOps.Count == 0)
                        {
                            continue;
                        }

                        string titreSignet = detector.FoundOps[0];
                        PdfExplicitDestination destination = PdfExplicitDestination.CreateFit(page);
                        PdfOutline subOutline = parentOutline.AddOutline(titreSignet);
                        subOutline.AddDestination(destination);
                    }

                    hasChanges = true;
                }

                if (hasChanges)
                {
                    File.Copy(tempFile, cheminPdf, true);
                }
            }
            catch (Exception ex)
            {
                // Gestion d'erreur ou log silencieux pour ne pas bloquer l'exportation
                Console.WriteLine($"Erreur lors de la création des signets : {ex.Message}");
            }
            finally
            {
                if (File.Exists(tempFile))
                {
                    try { File.Delete(tempFile); } catch { }
                }
            }
        }
    }
}