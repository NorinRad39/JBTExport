using System.Collections.Generic;

namespace JBTExport
{
    internal static class ConfigSignets
    {
        // liste de mots-clés à détecter au centre de la page (police >= 10mm)
        public static readonly List<string> MotsClesCentraux = new List<string>
        {
            "OP",
            "PLAN DE PLONGEE",
            "PREPA BRUT",
            "USINAGE"
        };

        // d'autres listes ici si besoin plus tard
    }
}