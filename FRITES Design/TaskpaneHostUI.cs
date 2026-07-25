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

        public string PendingVirtualComponent { get; private set; }
        public Part PendingVirtualComponentPart { get; private set; }

        private PreviewForm preview = new PreviewForm();

        private object lastHoveredModel = null;

        private AssemblyDoc _dragAssembly;

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

            treeListView1.ChildrenGetter = GetTreeChildren;
            treeListView1.CanExpandGetter = CanExpandTree;
            PartName.AspectGetter = GetPartNameAspect;
            PartName.ImageGetter = GetPartNameImage;
            SKU.AspectGetter = GetSkuAspect;
            downloaded.AspectGetter = GetDownloadedAspect;
            downloaded.ImageGetter = GetDownloadedImage;
        }

        private System.Collections.IEnumerable GetTreeChildren(object x)
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
                return c.Categories.Cast<object>().Concat(c.Parts);
            }
            return null;
        }

        private bool CanExpandTree(object x)
        {
            if (x is Category c)
                return dataManager.DoesCategoryHaveChildren(c.Id);
            return false;
        }

        private object GetPartNameAspect(object x)
        {
            if (x is Category c) return c.Name;
            if (x is Part p) return p.Name;
            return "";
        }

        private object GetPartNameImage(object x)
        {
            if (x is Category)
                return "folder";

            if (x is Part p)
            {
                if (!imageList1.Images.ContainsKey(p.Sku) && File.Exists(p.ThumbnailLink))
                {
                    imageList1.Images.Add(p.Sku, Image.FromFile(p.ThumbnailLink));
                }
                return p.Sku;
            }
            return null;
        }

        private object GetSkuAspect(object x)
        {
            if (x is Part p) return p.Sku;
            return "";
        }

        private object GetDownloadedAspect(object x)
        {
            return "";
        }

        private object GetDownloadedImage(object x)
        {
            if (x is Part p)
            {
                string path = Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData), "FRITES Design", "Step", p.Sku);
                if (Directory.Exists(path))
                    return "check";
            }
            return "";
        }

        public void RefreshTree()
        {
            var roots = dataManager.GetRootCategories();

            treeListView1.SetObjects(roots);
        }

        private static string GetLibrarySetupSuppressionPath()
        {
            return Path.Combine(
                System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData),
                "FRITES Design",
                "LibrarySetupForm.skipped");
        }

        private static void SuppressLibrarySetupForm()
        {
            var path = GetLibrarySetupSuppressionPath();
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, DateTime.UtcNow.ToString("O"));
        }

        private static bool ShouldShowLibrarySetupForm()
        {
            return !File.Exists(GetLibrarySetupSuppressionPath());
        }

        private void updateButton_Click(object sender, EventArgs e)
        {
            var loading = new LoadingForm();
            loading.Shown += async (_, __) => await OnUpdateLoadingShown(loading);
            loading.ShowDialog(this);

            if (ShouldShowLibrarySetupForm())
            {
                var setupForm = new LibrarySetupForm();
                DialogResult result = setupForm.ShowDialog(this);

                if (result != DialogResult.OK)
                {
                    SuppressLibrarySetupForm();
                    return;
                }

                SuppressLibrarySetupForm();


                Stopwatch stopwatch = Stopwatch.StartNew();

                var parts = dataManager.GetCommonlyUsedParts();

                loading = new LoadingForm();
                loading.SetLabel("Downloading and converting parts...");
                loading.Shown += async (_, __) => await OnDownloadLoadingShown(loading, parts);
                loading.ShowDialog(this);

                stopwatch.Stop();

                MessageBox.Show(
                    $"Library setup completed successfully.\n\nTime taken: {stopwatch.Elapsed}",
                    "Update Complete",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
            }
        }

        private async Task OnUpdateLoadingShown(LoadingForm loading)
        {
            try
            {
                var progress = new Progress<int>(loading.SetProgress);
                await dataManager.UpdateParts(progress);
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.ToString());
            }
            finally
            {
                loading.Close();
                RefreshTree();
                loading.Dispose();
            }
        }

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

                var results = dataManager.QueryParts(searchTextBox.Text);
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

        private void downloadButton_ClickAsync(object sender, EventArgs e)
        {
            var selectedParts = treeListView1.SelectedObjects.OfType<Part>().ToList();

            if (!selectedParts.Any())
            {
                MessageBox.Show("Select one or more parts before clicking download.");
                return;
            }

            var loading = new LoadingForm();
            loading.SetLabel("Downloading and converting parts...");
            loading.Shown += async (_, __) => await OnDownloadLoadingShown(loading, selectedParts);
            loading.ShowDialog(this);

        }

        private async Task OnDownloadLoadingShown(LoadingForm loading, List<Part> selectedParts)
        {
            const double AverageSecondsPerPart = 12.0;

            try
            {
                int total = selectedParts.Count;

                for (int i = 0; i < total; i++)
                {
                    Part part = selectedParts[i];

                    var progress = new Progress<int>(p =>
                    {
                        double overall = (i + p / 100.0) / total;
                        loading.SetProgress((int)(overall * 100));

                        // ETA
                        double remainingParts = (total - i - 1) + (100 - p) / 100.0;
                        double remainingSeconds = remainingParts * AverageSecondsPerPart;

                        loading.SetETA(TimeSpan.FromSeconds(remainingSeconds));
                    });

                    await PartDownloader.DownloadPart(SwApp, part, progress);
                }

                loading.SetETA(TimeSpan.Zero);
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.ToString());
            }
            finally
            {
                loading.Close();
                loading.Dispose();
            }
        }

        private void treeListView1_ItemDrag(object sender, ItemDragEventArgs e)
        {
            ModelDoc2 model = (ModelDoc2)SwApp.ActiveDoc;

            if (model == null ||
                model.GetType() != (int)swDocumentTypes_e.swDocASSEMBLY)
                return;

            _dragAssembly = (AssemblyDoc)model;
            _dragAssembly.FileDropPostNotify += OnFileDropPostNotify;


            string appData = System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData);

            string stepDir = Path.Combine(appData, "FRITES Design", "Step");

            string partDir = Path.Combine(stepDir, selectedPart.Sku);

            if (!Directory.Exists(partDir))
            {
                return;
            }

            string file = Path.Combine(partDir, selectedPart.Sku + ".SLDPRT");

            PendingVirtualComponent = file;
            PendingVirtualComponentPart = selectedPart;

            var data = new DataObject();
            data.SetData(DataFormats.FileDrop, new[] { file });


            DragDropEffects result = DoDragDrop(data, DragDropEffects.Copy);

        }

        private int OnFileDropPostNotify()
        {

            if (_dragAssembly == null || string.IsNullOrEmpty(PendingVirtualComponent))
                return 0;

            object[] components = (object[])_dragAssembly.GetComponents(false);

            foreach (Component2 comp in components)
            {
                if (string.Equals(
                comp.GetPathName(),
                PendingVirtualComponent,
                StringComparison.OrdinalIgnoreCase))
                {
                    bool success = comp.MakeVirtual2(true);
                    comp.Name2 = PendingVirtualComponentPart.Sku;
                    ModelDoc2 model = (ModelDoc2)comp.GetModelDoc2();
                    CustomPropertyManager props =
                            model.Extension.CustomPropertyManager[""];

                    props.Set2("Part Number", PendingVirtualComponentPart.Sku);
                    props.Set2("Description", PendingVirtualComponentPart.Name);
                    comp.ComponentReference = PendingVirtualComponentPart.Sku;

                    //props.Set2("Vendor", PendingVirtualComponentPart.Vendor); TODO: Add Vendor property to Part class and database
                    break;
                }
            }

            this.BeginInvoke(new Action(() =>
            {
                if (_dragAssembly != null)
                {
                    _dragAssembly.FileDropPostNotify -= OnFileDropPostNotify;
                    _dragAssembly = null;
                }

                PendingVirtualComponent = null;
                PendingVirtualComponentPart = null;
            }));


            return 0;
        }

        private void deleteButton_Click(object sender, EventArgs e)
        {
            var selectedParts = treeListView1.SelectedObjects.OfType<Part>().ToList();

            if (!selectedParts.Any())
            {
                MessageBox.Show("Select one or more parts before clicking delete.");
                return;
            }

            foreach (var part in selectedParts)
            {
                string path = Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData), "FRITES Design", "Step", part.Sku);
                if (Directory.Exists(path))
                {
                    try
                    {
                        Directory.Delete(path, true);
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show($"Failed to delete part {part.Name} ({part.Sku}): {ex.Message}");
                    }
                }
            }
        }

        private void openInBrowserButton_Click(object sender, EventArgs e)
        {
            var selectedParts = treeListView1.SelectedObjects.OfType<Part>().ToList();

            if (!selectedParts.Any())
            {
                MessageBox.Show("Select one or more parts before clicking open in browser.");
                return;
            }

            foreach (var part in selectedParts)
            {
                try
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = part.ProductPageLink,
                        UseShellExecute = true
                    });
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Failed to open URL {part.ProductPageLink}: {ex.Message}");
                }
            }
        }
    }
}