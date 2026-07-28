using FRITES.Core;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;

class Program
{
    [STAThread]
    static int Main(string[] args)
    {
        try
        {
            var jobs = ImportJob.LoadJobs(args[0]);

            var sw = SolidworksLauncher.StartNewInstance();


            sw.Visible = true;
            sw.UserControl = true;

            sw.DocumentVisible(false, (int)swDocumentTypes_e.swDocPART);
            sw.DocumentVisible(false, (int)swDocumentTypes_e.swDocASSEMBLY);


            try
            {
                ImportRunner.Run(sw, jobs, job =>
                {
                    Console.WriteLine($"DONE:{job.Sku}");
                });
            }
            finally
            {
                sw.ExitApp();
                Marshal.FinalReleaseComObject(sw);
            }

            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex.ToString());

            File.WriteAllText(
                Path.Combine(Path.GetTempPath(), "ImporterCrash.txt"),
                ex.ToString());

            return 1;
        }
    }
}