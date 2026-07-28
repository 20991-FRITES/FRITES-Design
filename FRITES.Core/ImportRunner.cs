using SolidWorks.Interop.sldworks;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FRITES.Core
{
    public static class ImportRunner
    {
        public static void Run(
            SldWorks sw,
            IEnumerable<ImportJob> jobs,
            Action<ImportJob> completed = null)
        {
            foreach (var job in jobs)
            {
                PartDownloader.ImportStep(
                    sw,
                    job.Sku,
                    job.Name,
                    job.StepFile,
                    false,
                    job.Material);

                completed?.Invoke(job);
            }
        }
    }
}
