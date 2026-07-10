using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TopSolid.Kernel.Automating;
using TSH = TopSolid.Kernel.Automating.TopSolidHost;
using OutilsTs;

namespace JBTExport
{
    internal class Program
    {
        static void Main(string[] args)
        {
            var connector = new StartConnect();

            connector.ConnectionTopsolid();

            if (TSH.IsConnected)
            {
                Console.WriteLine("TSH is connected");
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

    }


}







