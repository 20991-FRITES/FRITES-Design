using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swpublished;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace FRITES_Design
{
    [Guid("DB8747A5-46E2-4C3B-8A2F-56019D5866D0")]
    public class TaskpaneIntegration : SwAddin
    {
        #region Private members

        private int mSwCookie;
        private TaskpaneView mTaskpaneView;

        private TaskpaneHostUI mTaskpaneHost;

        private SldWorks mSolidWorksApplication;

        private DataManager mDataManager;

        #endregion

        #region Public members

        public const string SWTASKPANE_PROGID = "FRITES_Design.Taskpane";

        #endregion

        #region SW Connect/Disconnect
        public bool ConnectToSW(object ThisSW, int Cookie)
        {
            mSolidWorksApplication = (SldWorks)ThisSW;

            mSwCookie = Cookie;

            var ok = mSolidWorksApplication.SetAddinCallbackInfo2(0, this, Cookie);

            mDataManager = new DataManager();
            mDataManager.SetupDB();

            LoadUI();
            return true;
        }

        public bool DisconnectFromSW()
        {
            UnloadUI();
            return true;
        }

        #endregion

        #region Load/unload UI
        private void LoadUI()
        {
            var assemblyFolder = Path.GetDirectoryName(
    new Uri(typeof(TaskpaneIntegration).Assembly.CodeBase).LocalPath);

            var iconsFolder = Path.Combine(assemblyFolder, "taskpane_icons");

            object imageList = new[]
            {
                 Path.Combine(iconsFolder, "icon_20x20.png"),
                 Path.Combine(iconsFolder, "icon_32x32.png"),
                 Path.Combine(iconsFolder, "icon_40x40.png"),
                 Path.Combine(iconsFolder, "icon_64x64.png"),
                 Path.Combine(iconsFolder, "icon_96x96.png"),
                 Path.Combine(iconsFolder, "icon_128x128.png")
            };

            mTaskpaneView = mSolidWorksApplication.CreateTaskpaneView3(
                imageList,
                "FRITES Design");

            mTaskpaneHost = (TaskpaneHostUI)mTaskpaneView.AddControl(SWTASKPANE_PROGID, string.Empty);
            mTaskpaneHost.SwApp = mSolidWorksApplication;
            mTaskpaneHost.dataManager = mDataManager;

            //mTaskpaneHost.UpdateList();
            mTaskpaneHost.RefreshTree();
        }

        private void UnloadUI()
        {
            mTaskpaneHost = null;

            mTaskpaneView.DeleteView();

            Marshal.ReleaseComObject(mTaskpaneView);

            mTaskpaneView = null;
        }
        #endregion

        #region COM Registration

        [ComRegisterFunction()]
        private static void ComRegister(Type t)
        {
            var keyPath = string.Format(@"SOFTWARE\SolidWorks\AddIns\{0:b}", t.GUID);

            using (var rk = Microsoft.Win32.Registry.LocalMachine.CreateSubKey(keyPath))
            {
                // Load at SW startup
                rk.SetValue(null, 1);
                rk.SetValue("Title", "FRITES Design");
                rk.SetValue("Description", "FRITES Design library");
            }
        }

        [ComUnregisterFunction()]
        private static void ComUnregister(Type t)
        {
            var keyPath = string.Format(@"SOFTWARE\SolidWorks\AddIns\{0:b}", t.GUID);

            Microsoft.Win32.Registry.LocalMachine.DeleteSubKeyTree(keyPath);
        }

        #endregion
    }
}
