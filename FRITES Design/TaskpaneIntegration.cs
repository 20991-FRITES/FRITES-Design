using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swpublished;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace FRITES_Design
{
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
            var imagePath = Path.Combine(Path.GetDirectoryName(typeof(TaskpaneIntegration).Assembly.CodeBase).Replace(@"file:\", string.Empty), "logo-small.png");
            mTaskpaneView = mSolidWorksApplication.CreateTaskpaneView2(imagePath, "FRITES Design");

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
