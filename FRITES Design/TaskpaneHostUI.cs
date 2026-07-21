using BrightIdeasSoftware;
using FRITES_Design.Properties;
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
using static System.Windows.Forms.VisualStyles.VisualStyleElement.StartPanel;


namespace FRITES_Design
{


    [ProgId(TaskpaneIntegration.SWTASKPANE_PROGID)]
    public partial class TaskpaneHostUI : UserControl
    {
        public SldWorks SwApp { get; set; }
        public DataManager dataManager { get; set; }

        private Part selectedPart;
        private readonly Dictionary<string, Image> imageCache = new Dictionary<string, Image>();
        private bool searching = false;



        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int SendMessage(IntPtr hWnd, int msg, int wParam, string lParam);

        private const int EM_SETCUEBANNER = 0x1501;

        public TaskpaneHostUI()
        {
            InitializeComponent();
        }

        private void TaskpaneHostUI_Load(object sender, EventArgs e)
        {
            // Required for some .NET Framework projects
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
            SendMessage(searchTextBox.Handle, EM_SETCUEBANNER, 0, "Search...");

            imageList1.Images.Add("folder", Properties.Resources.folder);
            imageList1.Images.Add("check", Properties.Resources.check);

            treeListView1.ChildrenGetter = x =>
            {
                if (x is Category c)
                {
                    if (!searching)
                    {
                        if (!c.IsLoaded)
                        {
                            c.Categories.AddRange(dataManager.GetChildCategories(c.Id));
                            c.Parts.AddRange(dataManager.GetParts(c.Id));

                            c.IsLoaded = true;
                        }
                    }
                    return c.Categories.Cast<object>()
                                       .Concat(c.Parts);
                }

                return null;
            };

            treeListView1.CanExpandGetter = x =>
            {
                if (x is Category c)
                    return dataManager.DoesCategoryHaveChildren(c.Id);

                return false;
            };

            PartName.AspectGetter = x =>
            {
                if (x is Category c)
                    return c.Name;

                if (x is Part p)
                    return p.Name;

                return "";
            };

            PartName.ImageGetter = x =>
            {
                if (x is Category)
                    return "folder";

                if (x is Part p)
                {
                    if (!imageList1.Images.ContainsKey(p.Sku))
                    {
                        imageList1.Images.Add(p.Sku, Image.FromFile(p.ThumbnailLink));
                    }

                    return p.Sku;
                }

                return null;
            };

            SKU.AspectGetter = x =>
            {
                if (x is Part p)
                    return p.Sku;

                return "";
            };

            downloaded.AspectGetter = x =>
            {
                return "";
            };

            downloaded.ImageGetter = x =>
            {
                if (x is Part p)
                    // Check if the file has been downloaded
                    if (Directory.Exists(Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData), "FRITES Design", "Step", p.Sku)))
                        return "check";

                return "";
            };
        }

        public void RefreshTree()
        {
            var roots = dataManager.GetRootCategories();

            treeListView1.SetObjects(roots);
        }

        private async Task DownloadPart(Part part, IProgress<int> progress)
        {
            progress?.Report(0);

            string appData = System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData);
            string stepDir = Path.Combine(appData, "FRITES Design", "Step");

            Directory.CreateDirectory(stepDir);

            string partDir = Path.Combine(stepDir, part.Sku);

            SwApp.DocumentVisible(false, (int)swDocumentTypes_e.swDocPART);
            SwApp.DocumentVisible(false, (int)swDocumentTypes_e.swDocASSEMBLY);

            string localPartPath;

            try
            {
                progress?.Report(5);

                localPartPath = Path.Combine(partDir, part.Sku + ".sldprt");

                if (!File.Exists(localPartPath))
                {
                    Directory.CreateDirectory(partDir);

                    Uri uri = new Uri(part.StepLink);
                    string zipFileName = Path.GetFileName(uri.LocalPath);
                    string zipPath = Path.Combine(stepDir, zipFileName);

                    try
                    {
                        progress?.Report(10);

                        using (HttpClient client = new HttpClient())
                        using (Stream stream = await client.GetStreamAsync(uri))
                        using (FileStream file = File.Create(zipPath))
                        {
                            await stream.CopyToAsync(file);
                        }

                        progress?.Report(40);

                        ZipFile.ExtractToDirectory(zipPath, partDir);

                        progress?.Report(55);

                        string stepFile = Directory
                            .EnumerateFiles(partDir, "*.step", SearchOption.AllDirectories)
                            .Concat(Directory.EnumerateFiles(partDir, "*.stp", SearchOption.AllDirectories))
                            .FirstOrDefault();

                        if (stepFile == null)
                            throw new FileNotFoundException("No STEP file found in the archive.");

                        progress?.Report(65);

                        ImportStepData swImportStepData = (ImportStepData)SwApp.GetImportFileData(stepFile);
                        swImportStepData.MapConfigurationData = true;

                        int loadErrors = 0;
                        ModelDoc2 stepDoc = (ModelDoc2)SwApp.LoadFile4(stepFile, "r", swImportStepData, ref loadErrors);

                        if (stepDoc == null)
                            throw new Exception($"Failed to open STEP file. Error: {loadErrors}");

                        progress?.Report(80);

                        string savePath = Path.Combine(partDir, part.Sku + ".sldprt");

                        ModelDocExtension ext = stepDoc.Extension;

                        int saveErrors = 0;
                        int saveWarnings = 0;

                        bool saveSuccess = ext.SaveAs(
                            savePath,
                            (int)swSaveAsVersion_e.swSaveAsCurrentVersion,
                            (int)swSaveAsOptions_e.swSaveAsOptions_Silent,
                            null,
                            ref saveErrors,
                            ref saveWarnings);

                        SwApp.CloseDoc(stepDoc.GetTitle());

                        if (!saveSuccess)
                            throw new Exception($"Failed to save part. Errors: {saveErrors}");

                        progress?.Report(90);

                        localPartPath = savePath;
                    }
                    finally
                    {
                        if (File.Exists(zipPath))
                            File.Delete(zipPath);
                    }
                }

                progress?.Report(95);

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
                    throw new Exception($"Failed to preload component. Error: {preloadErrors}");

                progress?.Report(100);
            }
            finally
            {
                SwApp.DocumentVisible(true, (int)swDocumentTypes_e.swDocPART);
                SwApp.DocumentVisible(true, (int)swDocumentTypes_e.swDocASSEMBLY);
            }
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

        }

        private PreviewForm preview = new PreviewForm();

        private object lastHoveredModel = null;

        private void treeListView1_MouseMove(object sender, MouseEventArgs e)
        {
            var hitTest = treeListView1.OlvHitTest(e.Location.X, e.Location.Y);

            if (hitTest.RowObject == lastHoveredModel)
                return;

            lastHoveredModel = hitTest.RowObject;

            if (hitTest.RowObject == null)
            {
                preview.Hide();
                return;
            }
            if (hitTest.RowObject is Part part)
            {
                if (hitTest.Item != null)
                {
                    ShowPreview(hitTest.Item, part);
                }
                else
                {
                    preview.Hide();
                }
            }
            else
            {
                preview.Hide();
            }
        }

        private void ShowPreview(ListViewItem item, Part part)
        {
            if (string.IsNullOrEmpty(part.ImageLink)) return;

            Image previewImage = null;

            // 1. Check memory cache first (Instantaneous)
            if (imageCache.ContainsKey(part.ImageLink))
            {
                previewImage = imageCache[part.ImageLink];
            }
            else if (File.Exists(part.ImageLink))
            {
                try
                {
                    // Load asynchronously/non-locking via memory stream to keep it lightweight
                    byte[] bytes = File.ReadAllBytes(part.ImageLink);
                    using (MemoryStream ms = new MemoryStream(bytes))
                    {
                        Image loadedImg = Image.FromStream(ms);
                        // Cache a clone so we can safely manage memory
                        previewImage = (Image)loadedImg.Clone();
                        imageCache[part.ImageLink] = previewImage;
                    }
                }
                catch
                {
                    return; // Handle corrupt files gracefully
                }
            }

            if (previewImage == null) return;

            // 2. Set the data on your preview form
            preview.SetData(previewImage, part.Name, part.Sku);

            // 3. Position and display
            Rectangle bounds = item.Bounds;
            Point location = treeListView1.PointToScreen(
                new Point(bounds.Left - preview.Width - 2,
                          bounds.Top + (bounds.Height - preview.Height) / 2));

            preview.Location = location;

            if (!preview.Visible)
            {
                preview.Show();
            }
        }

        private void treeListView1_MouseLeave(object sender, EventArgs e)
        {
            Debug.WriteLine("[PreviewLog] MouseLeave fired. Hiding preview.");
            preview.Hide();
            lastHoveredModel = null;
        }

        private void refreshButton_Click(object sender, EventArgs e)
        {
            RefreshTree();
        }

        private void searchTextBox_TextChanged(object sender, EventArgs e)
        {
            if (string.IsNullOrWhiteSpace(searchTextBox.Text))
            {
                searching = false;
                RefreshTree();
            }
            else
            {
                searching = true;

                var results = dataManager.query_parts(searchTextBox.Text);
                var roots = BuildSearchTree(results);

                treeListView1.SetObjects(roots);
                treeListView1.ExpandAll();
            }
        }

        private List<Category> BuildSearchTree(List<Part> parts)
        {
            var roots = new List<Category>();

            foreach (var part in parts)
            {
                // Build the path from the part's category to the root
                var path = new Stack<Category>();

                Category current = dataManager.GetCategoryById(part.CategoryId);

                while (current != null)
                {
                    path.Push(current);

                    if (current.ParentId == null)
                        break;

                    current = dataManager.GetCategoryById(current.ParentId.Value);
                }

                // Walk down the path, creating folders as needed
                List<Category> currentLevel = roots;
                Category currentNode = null;

                while (path.Count > 0)
                {
                    var cat = path.Pop();

                    var existing = currentLevel.FirstOrDefault(c => c.Id == cat.Id);

                    if (existing == null)
                    {
                        existing = new Category
                        {
                            Id = cat.Id,
                            Name = cat.Name,
                            ParentId = cat.ParentId
                        };

                        currentLevel.Add(existing);
                    }

                    currentNode = existing;
                    currentLevel = existing.Categories;
                }

                // Finally add the matching part
                currentNode.Parts.Add(part);
            }

            return roots;
        }

        private void treeListView1_SelectedIndexChanged(object sender, EventArgs e)
        {
            selectedPart = treeListView1.SelectedObject as Part;
        }

        private async void downloadButton_ClickAsync(object sender, EventArgs e)
        {
            var selectedParts = treeListView1.SelectedObjects
                                 .OfType<Part>()
                                 .ToList();

            if (!selectedParts.Any())
            {
                MessageBox.Show("Select one or more parts before clicking download.");
                return;
            }

            using (var loading = new LoadingForm())
            {
                loading.SetLabel("Downloading and converting parts...");

                loading.Shown += async (_, __) =>
                {
                    try
                    {
                        int total = selectedParts.Count;

                        for (int i = 0; i < total; i++)
                        {
                            Part part = selectedParts[i];

                            var progress = new Progress<int>(p =>
                            {
                                // p is 0-100 for this part
                                double overall = (i + p / 100.0) / total;
                                loading.SetProgress((int)(overall * 100));
                            });

                            await DownloadPart(part, progress);
                        }
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
        }

        private void treeListView1_ItemDrag(object sender, ItemDragEventArgs e)
        {
            if (selectedPart == null)
            {
                return;
            }

            string appData = System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData);

            string stepDir = Path.Combine(appData, "FRITES Design", "Step");

            string partDir = Path.Combine(stepDir, selectedPart.Sku);

            if (!Directory.Exists(partDir)) {
                return;
            }

            string file = Path.Combine(partDir, selectedPart.Sku + ".SLDPRT");

            var data = new DataObject();
            data.SetData(DataFormats.FileDrop, new[] { file });

            DragDropEffects result = DoDragDrop(data, DragDropEffects.Copy);

            Debug.WriteLine(result);
        }
    }


}