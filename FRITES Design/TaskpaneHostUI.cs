using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace FRITES_Design
{
    [ProgId(TaskpaneIntegration.SWTASKPANE_PROGID)]
    public partial class TaskpaneHostUI : UserControl
    {
        public SldWorks SwApp { get; set; }
        public DataManager dataManager { get; set; }

        private string query = string.Empty;
        private Part selectedPart;
        private List<Part> partList;
        public TaskpaneHostUI()
        {
            InitializeComponent();
        }

        private void TaskpaneHostUI_Load(object sender, EventArgs e)
        {
            insertButton.Enabled = false;

            // Required for some .NET Framework projects
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;

            listView1.View = System.Windows.Forms.View.Details;
            listView1.UseCompatibleStateImageBehavior = false;

            imageList1.ImageSize = new Size(48, 48);
            listView1.SmallImageList = imageList1;
        }

        private void searchBar_TextChanged(object sender, EventArgs e)
        {
            query = searchBar.Text;
            update_list();
        }

        private async void insertButton_Click(object sender, EventArgs e)
        {
            ModelDoc2 model = (ModelDoc2)SwApp.ActiveDoc;

            if (model == null)
            {
                MessageBox.Show("No document is open.");
                return;
            }

            if (model.GetType() != (int)swDocumentTypes_e.swDocASSEMBLY)
            {
                MessageBox.Show("The active document is not an assembly.");
                return;
            }

            if (string.IsNullOrEmpty(model.GetPathName()))
            {
                MessageBox.Show("Please save the assembly before inserting a component.");
                return;
            }

            AssemblyDoc assembly = (AssemblyDoc)model;

            string appData = System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData);

            string stepDir = Path.Combine(appData, "FRITES Design", "Step");

            Directory.CreateDirectory(stepDir);

            string partDir = Path.Combine(stepDir, selectedPart.Sku);

            // Every document opened between here and the "finally" below (STEP import,
            // and the preload before AddComponent5) stays hidden -- only the SolidWorks
            // application window itself remains visible/untouched. A STEP file can import
            // as either a part or an assembly, so both types are covered.
            SwApp.DocumentVisible(false, (int)swDocumentTypes_e.swDocPART);
            SwApp.DocumentVisible(false, (int)swDocumentTypes_e.swDocASSEMBLY);

            string localPartPath;

            try
            {
                localPartPath = Path.Combine(partDir, selectedPart.Sku + ".sldprt");

                if (!File.Exists(localPartPath))
                {
                    Directory.CreateDirectory(partDir);

                    Uri uri = new Uri(selectedPart.StepLink);
                    string zipFileName = Path.GetFileName(uri.LocalPath);
                    string zipPath = Path.Combine(stepDir, zipFileName);

                    try
                    {
                        using (HttpClient client = new HttpClient())
                        using (Stream stream = await client.GetStreamAsync(uri))
                        using (FileStream file = File.Create(zipPath))
                        {
                            await stream.CopyToAsync(file);
                        }

                        ZipFile.ExtractToDirectory(zipPath, partDir);

                        // Find the STEP file
                        string stepFile = Directory
                            .EnumerateFiles(partDir, "*.step", SearchOption.AllDirectories)
                            .Concat(Directory.EnumerateFiles(partDir, "*.stp", SearchOption.AllDirectories))
                            .FirstOrDefault();

                        if (stepFile == null)
                            throw new FileNotFoundException("No STEP file found in the archive.");

                        // STEP files need the dedicated import pipeline (LoadFile4 + ImportStepData),
                        // not OpenDoc6 -- OpenDoc6 has no repair/diagnosis hook and fails outright
                        // on files that need it (swFileRequiresRepairError).
                        ImportStepData swImportStepData = (ImportStepData)SwApp.GetImportFileData(stepFile);
                        swImportStepData.MapConfigurationData = true;

                        int loadErrors = 0;
                        object loadedDoc = SwApp.LoadFile4(stepFile, "r", swImportStepData, ref loadErrors);
                        ModelDoc2 stepDoc = (ModelDoc2)loadedDoc;

                        if (stepDoc == null)
                        {
                            throw new Exception($"Failed to open STEP file. Error: {loadErrors}");
                        }

                        if (stepDoc.GetType() != (int)swDocumentTypes_e.swDocPART)
                        {
                            SwApp.CloseDoc(stepDoc.GetTitle());
                            throw new Exception("STEP file did not import as a part.");
                        }

                        string savePath = Path.Combine(partDir, selectedPart.Sku + ".sldprt");

                        ModelDocExtension ext = stepDoc.Extension;

                        int saveErrors = 0;
                        int saveWarnings = 0;

                        AdvancedSaveAsOptions adv =
                            (AdvancedSaveAsOptions)ext.GetAdvancedSaveAsOptions(0);

                        // Save referenced components too
                        adv.SaveAllAsCopy = false;


                        bool saveSuccess = ext.SaveAs3(
                            savePath,
                            (int)swSaveAsVersion_e.swSaveAsCurrentVersion,
                            (int)swSaveAsOptions_e.swSaveAsOptions_Silent,
                            null,
                            adv,
                            ref saveErrors,
                            ref saveWarnings);

                        // Close by the doc's own title, not the source STEP filename --
                        // SolidWorks assigns the in-memory title based on the part name,
                        // which usually doesn't match the STEP filename.
                        SwApp.CloseDoc(stepDoc.GetTitle());

                        if (!saveSuccess)
                        {
                            throw new Exception($"Failed to save part. Errors: {saveErrors}, Warnings: {saveWarnings}");
                        }

                        localPartPath = savePath;
                    }
                    finally
                    {
                        if (File.Exists(zipPath))
                            File.Delete(zipPath);
                    }
                }

                if (!File.Exists(localPartPath))
                {
                    throw new Exception($"Expected saved component not found at: {localPartPath}");
                }

                // AddComponent5 only reliably works from automation code if the component is
                // already resident in the session -- unlike the interactive Insert Component
                // flow, it does not load the model from disk itself when called this way.
                int preloadErrors = 0;
                int preloadWarnings = 0;
                ModelDoc2 preloadDoc = SwApp.OpenDoc6(
                    localPartPath,
                    (int)swDocumentTypes_e.swDocPART,
                    (int)swOpenDocOptions_e.swOpenDocOptions_Silent,
                    "",
                    ref preloadErrors,
                    ref preloadWarnings);

                if (preloadDoc == null)
                {
                    throw new Exception($"Failed to preload component before adding to assembly. Error: {preloadErrors}");
                }
            }
            finally
            {
                // Reset visibility so anything the user opens themselves afterward behaves normally
                SwApp.DocumentVisible(true, (int)swDocumentTypes_e.swDocPART);
                SwApp.DocumentVisible(true, (int)swDocumentTypes_e.swDocASSEMBLY);
            }

            // Make sure the assembly is the active document before adding to it
            int activateErrors = 0;
            SwApp.ActivateDoc3(
                model.GetTitle(),
                false,
                (int)swRebuildOnActivation_e.swUserDecision,
                ref activateErrors);

            Component2 comp = assembly.AddComponent5(
                localPartPath,
                (int)swAddComponentConfigOptions_e.swAddComponentConfigOptions_CurrentSelectedConfig,
                "",
                false,
                "",
                0, 0, 0);

            if (comp == null)
            {
                throw new Exception("AddComponent5 failed to add the component.");
            }

            // Embed the component directly into the assembly file -- since catalog parts are
            // never revised in place (only new SKUs are created), there's no benefit to keeping
            // an external file link, and this keeps the assembly shareable as a single file.
            bool madeVirtual = comp.MakeVirtual2(true);

            if (!madeVirtual)
            {
                throw new Exception("Failed to make component virtual.");
            }

            ModelDoc2 doc = (ModelDoc2)comp.GetModelDoc2();
            doc.SetTitle2(selectedPart.Sku);

            CustomPropertyManager props =
                doc.Extension.CustomPropertyManager[""];

            // For BOM
            props.Set2("Description", selectedPart.Name);
            props.Set2("Part Number", selectedPart.Sku);

            comp.Name2 = selectedPart.Sku;
            model.EditRebuild3();
        }

        public void update_list()
        {
            List<Part> parts = dataManager.query_parts(query);
            partList = parts;

            listView1.BeginUpdate();

            listView1.Items.Clear();
            imageList1.Images.Clear();

            foreach (Part part in parts)
            {
                int imageIndex = -1;

                try
                {
                    if (File.Exists(part.ImageLink))
                    {
                        using (Image image = Image.FromFile(part.ImageLink))
                        {
                            // Clone the image so the file isn't locked
                            imageList1.Images.Add((Image)image.Clone());
                        }

                        imageIndex = imageList1.Images.Count - 1;
                    }
                }
                catch
                {
                    // Ignore image load failures
                }

                ListViewItem item = new ListViewItem(part.Name);

                if (imageIndex >= 0)
                    item.ImageIndex = imageIndex;

                item.SubItems.Add(part.Sku);
                item.Tag = part;

                listView1.Items.Add(item);
            }

            listView1.EndUpdate();
        }

        private void updateButton_Click(object sender, EventArgs e)
        {
            using (var loading = new LoadingForm())
            {
                loading.Shown += async (_, __) =>
                {
                    try
                    {
                        var progress = new Progress<int>(value =>
                        {
                            loading.SetProgress(value);
                        });

                        await dataManager.update_parts(progress);
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show(ex.ToString());
                    }
                    finally
                    {
                        loading.Close();
                    }
                };

                loading.ShowDialog(this);
            }

            update_list();
        }

        private void resultList_SelectedIndexChanged(object sender, EventArgs e)
        {
            insertButton.Enabled = listView1.SelectedItems.Count > 0;

            if (listView1.SelectedItems.Count == 0)
            {
                selectedPart = null;
                return;
            }

            selectedPart = (Part)listView1.SelectedItems[0].Tag;
        }

        PreviewForm preview = new PreviewForm();
        private ListViewItem hoveredItem = null;

        private void listView1_MouseLeave(object sender, EventArgs e)
        {
            preview.Hide();
        }

        private void ShowPreview(ListViewItem item)
        {
            Part part = (Part)item.Tag;

            if (!File.Exists(part.ImageLink))
                return;

            using (var temp = Image.FromFile(part.ImageLink))
            {
                Image img = (Image)temp.Clone();

                preview.SetData(
                    img,
                    part.Name,
                    part.Sku);
            }

            Rectangle bounds = item.Bounds;

            // Bottom-left corner of the item, converted to screen coordinates
            Point location = listView1.PointToScreen(
                new Point(bounds.Left, bounds.Bottom + 2));

            preview.Location = location;
            preview.Show();
        }

        private void listView1_MouseMove(object sender, MouseEventArgs e)
        {
            ListViewItem item = listView1.GetItemAt(e.X, e.Y);

            if (item == hoveredItem)
                return; // Still over the same item

            hoveredItem = item;

            if (item == null)
            {
                preview.Hide();
                return;
            }

            ShowPreview(item);
        }
    }
}