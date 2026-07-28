using SolidWorks.Interop.sldworks;
using System;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Threading;

namespace FRITES.Core
{
    public static class SolidworksLauncher
    {
        private const string SolidWorksPath =
            @"C:\Program Files\SOLIDWORKS Corp\SOLIDWORKS\SLDWORKS.exe";

        [DllImport("ole32.dll")]
        private static extern int CreateBindCtx(
            uint reserved,
            out IBindCtx ppbc);

        public static SldWorks StartNewInstance(int timeoutSeconds = 60)
        {
            Process process = Process.Start(SolidWorksPath)
                ?? throw new Exception("Failed to start SolidWorks.");

            Stopwatch sw = Stopwatch.StartNew();

            while (sw.Elapsed < TimeSpan.FromSeconds(timeoutSeconds))
            {
                var app = GetSolidWorksFromProcess(process.Id);

                if (app != null && WaitUntilReady(app, 15))
                    return app;

                Thread.Sleep(250);
            }

            throw new TimeoutException("Timed out waiting for SolidWorks.");
        }

        private static bool WaitUntilReady(SldWorks app, int timeoutSeconds)
        {
            Stopwatch sw = Stopwatch.StartNew();

            while (sw.Elapsed < TimeSpan.FromSeconds(timeoutSeconds))
            {
                try
                {
                    // Any simple COM call works.
                    int pid = app.GetProcessID();

                    // Make sure the frame exists too.
                    var frame = app.Frame();

                    if (frame != null)
                        return true;
                }
                catch (COMException)
                {
                    // Still initializing.
                }

                Thread.Sleep(200);
            }

            return false;
        }

        private static SldWorks GetSolidWorksFromProcess(int processId)
        {
            string monikerName = $"SolidWorks_PID_{processId}";

            IBindCtx context = null;
            IRunningObjectTable rot = null;
            IEnumMoniker monikers = null;

            try
            {
                CreateBindCtx(0, out context);

                context.GetRunningObjectTable(out rot);

                rot.EnumRunning(out monikers);

                var moniker = new IMoniker[1];

                while (monikers.Next(1, moniker, IntPtr.Zero) == 0)
                {
                    string name = null;

                    try
                    {
                        moniker[0].GetDisplayName(
                            context,
                            null,
                            out name);
                    }
                    catch (UnauthorizedAccessException)
                    {
                        continue;
                    }

                    if (string.Equals(
                        name,
                        monikerName,
                        StringComparison.OrdinalIgnoreCase))
                    {
                        rot.GetObject(moniker[0], out object obj);

                        return (SldWorks)obj;
                    }
                }
            }
            finally
            {
                if (monikers != null)
                    Marshal.ReleaseComObject(monikers);

                if (rot != null)
                    Marshal.ReleaseComObject(rot);

                if (context != null)
                    Marshal.ReleaseComObject(context);
            }

            return null;
        }
    }
}