using SolidWorks.Interop.sldworks;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace FRITES.Core
{
    public static class ImportRunner
    {
        public static void Run(
            SldWorks sw,
            IEnumerable<ImportJob> jobs,
            Action<ImportJob> completed = null)
        {
            string appData = System.Environment.GetFolderPath(
                System.Environment.SpecialFolder.LocalApplicationData);

            foreach (var job in jobs)
            {
                try
                {
                    Console.WriteLine($"START:{job.Sku}");

                    string partDirFinal = Path.Combine(
                        appData,
                        "FRITES Design",
                        "Step",
                        job.Sku);

                    string partDirTemp = partDirFinal + ".tmp";

                    string outputFile = Path.Combine(
                        partDirTemp,
                        Path.GetFileNameWithoutExtension(job.StepFile) + ".sldprt");

                    PartDownloader.ImportStep(
                        sw,
                        job.Sku,
                        job.Name,
                        job.StepFile,
                        outputFile,
                        false,
                        job.Material,
                        job.Finish);
                }
                catch {
                    Console.WriteLine($"Error processing job for SKU: {job.Sku}");
                }
                finally
                {
                    completed?.Invoke(job);
                }
            }

            // Finalize each SKU once.
            foreach (string sku in jobs.Select(j => j.Sku).Distinct())
            {
                string partDirFinal = Path.Combine(
                    appData,
                    "FRITES Design",
                    "Step",
                    sku);

                string partDirTemp = partDirFinal + ".tmp";

                if (!Directory.Exists(partDirFinal) &&
                    Directory.Exists(partDirTemp))
                {
                    Directory.Move(partDirTemp, partDirFinal);
                }
            }
        }
    }
}