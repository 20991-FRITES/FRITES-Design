using BrightIdeasSoftware;
using FRITES.Core;
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
using System.Text.Json;
using System.Threading;
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

        private async void updateButton_Click(object sender, EventArgs e)
        {
            var loading = new LoadingForm();
            loading.Shown += async (_, __) => await OnUpdateLoadingShown(loading);
            loading.ShowDialog(this);

            if (!ShouldShowLibrarySetupForm())
                return;

            var setupForm = new LibrarySetupForm();

            if (setupForm.ShowDialog(this) != DialogResult.OK)
                return;

            SuppressLibrarySetupForm();

            Stopwatch stopwatch = Stopwatch.StartNew();

            var parts = dataManager.GetCommonlyUsedParts();

            if (setupForm.multithreadingEnabled)
            {
                List<ImportJob> jobs = null;

                loading = new LoadingForm();
                loading.SetLabel("Downloading parts...");

                loading.Shown += async (_, __) =>
                {
                    jobs = await DownloadPartsAsync(loading, parts);
                    loading.Close();
                };

                loading.ShowDialog(this);

                loading = new LoadingForm();
                loading.SetLabel("Importing parts...");

                loading.Shown += async (_, __) =>
                {
                    await RunMultiProcessImport(loading, jobs);
                    loading.Close();
                };

                loading.ShowDialog(this);
            }
            else
            {
                loading = new LoadingForm();
                loading.SetLabel("Downloading and converting parts...");
                loading.Shown += async (_, __) => await OnDownloadLoadingShown(loading, parts);
                loading.ShowDialog(this);
            }

            stopwatch.Stop();

            MessageBox.Show(
                $"Library setup completed successfully.\n\nTime taken: {stopwatch.Elapsed}",
                "Update Complete",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }


        private async Task RunMultiProcessImport(
    LoadingForm loading,
    List<ImportJob> jobs)
        {
            int workerCount = 2;

            var workerJobs = Enumerable.Range(0, workerCount)
                .Select(_ => new List<ImportJob>())
                .ToArray();

            for (int i = 0; i < jobs.Count; i++)
            {
                workerJobs[i % workerCount].Add(jobs[i]);
            }

            string jobsFolder = Path.Combine(
                System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData),
                "FRITES Design",
                "Jobs");

            Directory.CreateDirectory(jobsFolder);

            string importerExe = Path.Combine(
                Path.GetDirectoryName(typeof(TaskpaneIntegration).Assembly.Location),
                "FRITES-Importer.exe");

            int completed = 0;
            int total = jobs.Count;

            double averageSecondsPerPart = 8.0 / workerCount;

            void ReportProgress()
            {
                int value = Interlocked.Increment(ref completed);

                BeginInvoke(new Action(() =>
                {
                    loading.SetProgress(value * 100 / total);

                    double remaining =
                        (total - completed) * averageSecondsPerPart;

                    loading.SetETA(TimeSpan.FromSeconds(remaining));
                }));
            }

            DataReceivedEventHandler handler = (_, e) =>
            {
                if (e.Data?.StartsWith("DONE:") == true)
                {
                    ReportProgress();
                }
            };

            var processes = new List<(Process Process, string JobFile)>();

            //
            // Launch external workers (all except the last one)
            //
            for (int i = 0; i < workerCount - 1; i++)
            {
                string jobFile = Path.Combine(jobsFolder, $"worker{i}.json");

                ImportJob.SaveJobs(jobFile, workerJobs[i]);

                var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = importerExe,
                        Arguments = $"\"{jobFile}\"",
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        CreateNoWindow = true
                    }
                };

                process.OutputDataReceived += handler;

                process.Start();
                process.BeginOutputReadLine();

                processes.Add((process, jobFile));
            }

            //
            // Existing SOLIDWORKS becomes the final worker.
            //
            ImportRunner.Run(
                SwApp,
                workerJobs[workerCount - 1],
                job => ReportProgress());

            //
            // Wait for external workers.
            //
            await Task.Run(() =>
            {
                foreach (var (process, _) in processes)
                {
                    process.WaitForExit();

                    if (process.ExitCode != 0)
                    {
                        throw new Exception(
                            $"Importer worker exited with code {process.ExitCode}.");
                    }
                }
            });

            //
            // Cleanup.
            //
            foreach (var (process, jobFile) in processes)
            {
                process.Dispose();

                if (File.Exists(jobFile))
                    File.Delete(jobFile);
            }

            loading.SetETA(TimeSpan.Zero);
        }

        private async Task OnUpdateLoadingShown(LoadingForm loading)
        {
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();

            try
            {
                var progress = new Progress<int>(value =>
                {
                    loading.SetProgress(value);

                    if (value > 0)
                    {
                        var elapsed = stopwatch.Elapsed;
                        var totalEstimated = TimeSpan.FromTicks(elapsed.Ticks * 100L / value);
                        var eta = totalEstimated - elapsed;

                        loading.SetETA(eta);
                    }
                });

                await dataManager.UpdateParts(progress);
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.ToString());
            }
            finally
            {
                stopwatch.Stop();
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
                Stopwatch sw = Stopwatch.StartNew();
                sw.Start();
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

            // Fast lookup of created nodes
            var createdNodes = new Dictionary<int, Category>();

            // Cache the path for each category
            var pathCache = new Dictionary<int, List<Category>>();

            foreach (var part in parts)
            {
                if (!pathCache.TryGetValue(part.CategoryId, out var path))
                {
                    path = new List<Category>();

                    var current = dataManager.GetCategoryById(part.CategoryId);

                    while (current != null)
                    {
                        path.Add(current);

                        if (current.ParentId == null)
                            break;

                        current = dataManager.GetCategoryById(current.ParentId.Value);
                    }

                    path.Reverse();
                    pathCache[part.CategoryId] = path;
                }

                Category parent = null;

                foreach (var cat in path)
                {
                    if (!createdNodes.TryGetValue(cat.Id, out var node))
                    {
                        node = new Category
                        {
                            Id = cat.Id,
                            Name = cat.Name,
                            ParentId = cat.ParentId
                        };

                        createdNodes.Add(cat.Id, node);

                        if (parent == null)
                            roots.Add(node);
                        else
                            parent.Categories.Add(node);
                    }

                    parent = node;
                }

                parent.Parts.Add(part);
            }

            return roots;
        }

        private void treeListView1_SelectedIndexChanged(object sender, EventArgs e)
        {
            selectedPart = treeListView1.SelectedObject as Part;
        }

        private async void DownloadPartList(List<Part> parts)
        {
            var loading = new LoadingForm();
            loading.SetLabel("Downloading and converting parts...");
            loading.Shown += async (_, __) => await OnDownloadLoadingShown(loading, parts);
            loading.ShowDialog(this);
        }

        private void downloadButton_ClickAsync(object sender, EventArgs e)
        {
            var selectedParts = treeListView1.SelectedObjects.OfType<Part>().ToList();

            if (!selectedParts.Any())
            {
                MessageBox.Show("Select one or more parts before clicking download.");
                return;
            }

            DownloadPartList(selectedParts);
        }

        private async Task<List<ImportJob>> DownloadPartsAsync(
    LoadingForm loading,
    List<Part> selectedParts)
        {
            const double AverageSecondsPerPart = 1;

            var jobs = new List<ImportJob>();

            int total = selectedParts.Count;

            for (int i = 0; i < total; i++)
            {
                Part part = selectedParts[i];

                string stepFile = await PartDownloader.DownloadStepAsync(part);

                if (stepFile != null)
                {
                    jobs.Add(new ImportJob
                    {
                        Sku = part.Sku,
                        Name = part.Name,
                        StepFile = stepFile,
                        Material = part.Material
                    });
                }

                // Progress after finishing one file
                int completed = i + 1;

                loading.SetProgress(completed * 100 / total);

                double remainingSeconds =
                    (total - completed) * AverageSecondsPerPart;

                loading.SetETA(TimeSpan.FromSeconds(remainingSeconds));
            }

            loading.SetETA(TimeSpan.Zero);

            return jobs;
        }

        private async Task OnDownloadLoadingShown(LoadingForm loading, List<Part> selectedParts)
        {
            const double AverageSecondsPerPart = 12.0;

            SwApp.DocumentVisible(false, (int)swDocumentTypes_e.swDocPART);
            SwApp.DocumentVisible(false, (int)swDocumentTypes_e.swDocASSEMBLY);
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

                    string stepFile = await PartDownloader.DownloadStepAsync(part);
                    if (stepFile == null)
                        continue;
                    PartDownloader.ImportStep(SwApp, part.Sku, part.Name, stepFile, false, part.Material);
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
                SwApp.DocumentVisible(true, (int)swDocumentTypes_e.swDocPART);
                SwApp.DocumentVisible(true, (int)swDocumentTypes_e.swDocASSEMBLY);
            }
        }

        private void treeListView1_ItemDrag(object sender, ItemDragEventArgs e)
        {
            ModelDoc2 model = (ModelDoc2)SwApp.ActiveDoc;

            if (model == null ||
                model.GetType() != (int)swDocumentTypes_e.swDocASSEMBLY || selectedPart == null)
                return;

            _dragAssembly = (AssemblyDoc)model;
            _dragAssembly.FileDropPostNotify += OnFileDropPostNotify;


            string appData = System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData);

            string stepDir = Path.Combine(appData, "FRITES Design", "Step");

            string partDir = Path.Combine(stepDir, selectedPart.Sku);

            if (!Directory.Exists(partDir))
            {
                DownloadPartList(new List<Part> { selectedPart });
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