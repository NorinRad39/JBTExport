using System;
using System.Collections.Generic;
using iText.Kernel.Geom;
using iText.Kernel.Pdf.Canvas.Parser;
using iText.Kernel.Pdf.Canvas.Parser.Data;
using iText.Kernel.Pdf.Canvas.Parser.Listener;

namespace JBTExport
{
    public class Detector : IEventListener
    {
        private readonly Rectangle _pageSize;
        private readonly List<string> _targetTexts; // 🔑 Liste des textes à chercher
        public List<string> FoundOps { get; } = new List<string>();

        // Le constructeur accepte maintenant la liste des cibles
        public Detector(Rectangle pageSize, List<string> targetTexts)
        {
            _pageSize = pageSize;
            _targetTexts = targetTexts ?? new List<string>();
        }

        public void EventOccurred(IEventData data, EventType type)
        {
            if (type != EventType.RENDER_TEXT) return;

            var renderInfo = (TextRenderInfo)data;
            string text = renderInfo.GetText();

            if (string.IsNullOrEmpty(text)) return;

            // 1. On vérifie si le texte contient l'un des mots-clés de notre liste
            bool matchesTarget = false;
            foreach (var target in _targetTexts)
            {
                if (text.IndexOf(target, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    matchesTarget = true;
                    break; // Un seul match suffit
                }
            }

            if (matchesTarget)
            {
                // 2. Vérification de la taille (10 mm = ~28.3 points)
                float fontSize = renderInfo.GetFontSize();
                if (fontSize >= 28.3f)
                {
                    // 3. Vérification de la position (hauteur uniquement)
                    LineSegment baseline = renderInfo.GetBaseline();
                    float textY = baseline.GetStartPoint().Get(1);

                    // Bande centrale verticale : toute la largeur de la page
                    float minY = _pageSize.GetHeight() * 0.38f;
                    float maxY = _pageSize.GetHeight() * 0.62f;

                    if (textY >= minY && textY <= maxY)
                    {
                        FoundOps.Add(text.Trim());
                    }
                }
            }
        }

        public ICollection<EventType> GetSupportedEvents()
        {
            return new[] { EventType.RENDER_TEXT };
        }
    }
}