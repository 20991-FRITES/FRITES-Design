using FRITES.Core;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;


namespace FRITES_Design
{
    [Guid("7BD24269-4244-4E18-8724-C783DBCE8A90")]
    [ProgId(TaskpaneIntegration.SWTASKPANE_PROGID)]
    public partial class TaskpaneHostUI : UserControl
    {
        public SldWorks SwApp { get; set; }
        public DataManager dataManager { get; set; }

        private readonly Dictionary<string, Image> imageCache = new Dictionary<string, Image>();
        private bool searching;

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int SendMessage(IntPtr hWnd, int msg, int wParam, string lParam);

        private const int EM_SETCUEBANNER = 0x1501;

        public string PendingVirtualComponent { get; private set; }
        public Part PendingVirtualComponentPart { get; private set; }

        private PreviewForm preview = new PreviewForm();

        private object lastHoveredModel = null;

        private AssemblyDoc _dragAssembly;

        private readonly Stack<string> _backupStack = new Stack<string>();

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
            imageList1.Images.Add("folder-open", Properties.Resources.folder_open);
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

            if (x is Part p)
            {
                return VariantManager.GetVariants(p);
            }

            return null;
        }

        private bool CanExpandTree(object x)
        {
            if (x is Category c)
                return dataManager.DoesCategoryHaveChildren(c.Id);
            if (x is Part p)
                return VariantManager.GetVariants(p).Count() > 1;
            return false;
        }

        private object GetPartNameAspect(object x)
        {
            if (x is Category c) return c.Name;
            if (x is Part p) return p.Name;
            if (x is PartVariant v) return v.Name;
            return "";
        }

        private object GetPartNameImage(object x)
        {
            if (x is Category)
            {
                bool expanded = treeListView1.IsExpanded(x);
                return expanded ? "folder-open" : "folder";
            }

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
                string path =
                    Path.Combine(
                        System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData),
                        "FRITES Design", "Step", p.Sku);
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
            var groups = jobs
                .GroupBy(j => j.Sku)
                .ToList();

            int workerCount = 4;
            var workerJobs = Enumerable.Range(0, workerCount)
                .Select(_ => new List<ImportJob>())
                .ToArray();

            int worker = 0;

            foreach (var group in groups)
            {
                workerJobs[worker].AddRange(group);

                worker = (worker + 1) % workerCount;
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

            // Smoothed average seconds between completed parts.
            double averageSecondsPerCompletion = 0;
            const double SmoothingFactor = 0.2;

            var completionTimer = Stopwatch.StartNew();
            long lastCompletionTicks = completionTimer.ElapsedTicks;
            object etaLock = new object();

            void ReportProgress()
            {
                int value = Interlocked.Increment(ref completed);

                lock (etaLock)
                {
                    long now = completionTimer.ElapsedTicks;
                    double elapsedSeconds =
                        (now - lastCompletionTicks) / (double)Stopwatch.Frequency;

                    lastCompletionTicks = now;

                    if (averageSecondsPerCompletion == 0)
                    {
                        averageSecondsPerCompletion = elapsedSeconds;
                    }
                    else
                    {
                        averageSecondsPerCompletion =
                            averageSecondsPerCompletion * (1 - SmoothingFactor) +
                            elapsedSeconds * SmoothingFactor;
                    }
                }

                BeginInvoke(new Action(() =>
                {
                    loading.SetProgress(value * 100 / total);

                    double remaining =
                        (total - value) * averageSecondsPerCompletion;

                    loading.SetETA(TimeSpan.FromSeconds(Math.Max(0, remaining)));
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
                _ => ReportProgress());

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

            loading.SetProgress(100);
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
            var jobs = new List<ImportJob>();

            int total = selectedParts.Count;

            // Smoothed average download time (seconds)
            double averageSeconds = 0;
            const double SmoothingFactor = 0.2;

            for (int i = 0; i < total; i++)
            {
                Part part = selectedParts[i];

                var partTimer = Stopwatch.StartNew();

                string stepFolder = await PartDownloader.DownloadStepAsync(part);

                partTimer.Stop();

                if (stepFolder != null)
                {
                    foreach (string stepFile in PartDownloader.EnumerateStepFiles(stepFolder))
                    {
                        jobs.Add(new ImportJob
                        {
                            Sku = part.Sku,
                            Name = part.Name,
                            StepFile = stepFile,
                            Material = part.Material,
                            Finish = part.Finish
                        });
                    }
                }

                // Update smoothed average
                double elapsedSeconds = partTimer.Elapsed.TotalSeconds;

                if (averageSeconds == 0)
                {
                    averageSeconds = elapsedSeconds;
                }
                else
                {
                    averageSeconds =
                        averageSeconds * (1 - SmoothingFactor) +
                        elapsedSeconds * SmoothingFactor;
                }

                int completed = i + 1;

                loading.SetProgress(completed * 100 / total);

                double remainingSeconds =
                    (total - completed) * averageSeconds;

                loading.SetETA(TimeSpan.FromSeconds(remainingSeconds));
            }

            loading.SetProgress(100);
            loading.SetETA(TimeSpan.Zero);

            return jobs;
        }

        private async Task OnDownloadLoadingShown(LoadingForm loading, List<Part> selectedParts)
        {
            const double AverageSecondsPerPart = 12.0;

            // SwApp.DocumentVisible(false, (int)swDocumentTypes_e.swDocPART);
            // SwApp.DocumentVisible(false, (int)swDocumentTypes_e.swDocASSEMBLY);

            try
            {
                int total = selectedParts.Count;

                string appData = System.Environment.GetFolderPath(
                    System.Environment.SpecialFolder.LocalApplicationData);

                for (int i = 0; i < total; i++)
                {
                    Part part = selectedParts[i];

                    loading.SetProgress(i * 100 / total);

                    loading.SetETA(TimeSpan.FromSeconds(
                        (total - i) * AverageSecondsPerPart));

                    string stepFolder = await PartDownloader.DownloadStepAsync(part);

                    if (stepFolder == null)
                        continue;

                    string partDirFinal = Path.Combine(
                        appData,
                        "FRITES Design",
                        "Step",
                        part.Sku);

                    string partDirTemp = partDirFinal + ".tmp";

                    foreach (string stepFile in PartDownloader.EnumerateStepFiles(stepFolder))
                    {
                        string outputFile = Path.Combine(
                            partDirTemp,
                            Path.GetFileNameWithoutExtension(stepFile) + ".sldprt");

                        PartDownloader.ImportStep(
                            SwApp,
                            part.Sku,
                            part.Name,
                            stepFile,
                            outputFile,
                            false,
                            part.Material,
                            part.Finish);
                    }

                    if (!Directory.Exists(partDirFinal))
                    {
                        Directory.Move(partDirTemp, partDirFinal);
                    }
                }

                loading.SetProgress(100);
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

                // SwApp.DocumentVisible(true, (int)swDocumentTypes_e.swDocPART);
                // SwApp.DocumentVisible(true, (int)swDocumentTypes_e.swDocASSEMBLY);
            }
        }

        private void treeListView1_ItemDrag(object sender, ItemDragEventArgs e)
        {
            ModelDoc2 model = (ModelDoc2)SwApp.ActiveDoc;

            if (model == null ||
                model.GetType() != (int)swDocumentTypes_e.swDocASSEMBLY)
                return;

            string file;
            Part part;

            switch (treeListView1.SelectedObject)
            {
                case PartVariant variant:
                    file = variant.SldprtPath;
                    part = variant.Part;
                    part.Sku = variant.Name;
                    break;

                case Part p:
                {
                    var variants = VariantManager.GetVariants(p);

                    if (variants.Count == 0)
                    {
                        DownloadPartList(new List<Part> { p });
                        return;
                    }

                    if (variants.Count > 1)
                    {
                        MessageBox.Show("This part has multiple variants.");
                        return;
                    }

                    file = variants[0].SldprtPath;
                    part = p;
                    break;
                }

                default:
                    return;
            }

            _dragAssembly = (AssemblyDoc)model;
            _dragAssembly.FileDropPostNotify += OnFileDropPostNotify;

            PendingVirtualComponent = file;
            PendingVirtualComponentPart = part;

            var data = new DataObject();
            data.SetData(DataFormats.FileDrop, new[] { file });

            DoDragDrop(data, DragDropEffects.Copy);
        }

        private Component2 FindExistingVirtualComponent(AssemblyDoc assembly, string sku)
        {
            object[] components = (object[])assembly.GetComponents(false);

            foreach (Component2 comp in components)
            {
                if (!comp.IsVirtual)
                    continue;

                if (string.Equals(comp.ComponentReference, sku, StringComparison.Ordinal))
                    return comp;
            }

            return null;
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
                    Component2 component = comp;

                    Component2 existingVirtual =
                        FindExistingVirtualComponent(_dragAssembly, PendingVirtualComponentPart.Sku);

                    if (existingVirtual != null)
                    {
                        MathTransform t = comp.Transform2;

                        comp.Select4(false, null, false);
                        ((ModelDoc2)_dragAssembly).EditDelete();

                        string path = existingVirtual.GetPathName();

                        Component2 replacement = _dragAssembly.AddComponent5(
                            path,
                            (int)swAddComponentConfigOptions_e.swAddComponentConfigOptions_CurrentSelectedConfig,
                            "",
                            false,
                            "",
                            0, 0, 0);

                        replacement.Transform2 = t;
                        component = replacement;
                    }
                    else
                    {
                        component.MakeVirtual2(true);

                        component.Name2 = PendingVirtualComponentPart.Sku;
                        ModelDoc2 model = (ModelDoc2)component.GetModelDoc2();
                        CustomPropertyManager props =
                            model.Extension.CustomPropertyManager[""];

                        props.Set2("Part Number", PendingVirtualComponentPart.Sku);
                        props.Set2("Description", PendingVirtualComponentPart.Name);
                        component.ComponentReference = PendingVirtualComponentPart.Sku;
                    }

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
                string path =
                    Path.Combine(
                        System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData),
                        "FRITES Design", "Step", part.Sku);
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

        private string CreateBackup(ModelDoc2 model)
        {
            string backupPath = Path.Combine(
                Path.GetTempPath(),
                Path.GetFileNameWithoutExtension(model.GetPathName())
                + "_backup_" + Guid.NewGuid().ToString("N") + ".sldasm");

            int errors = 0, warnings = 0;

            bool saved = model.Extension.SaveAs3(
                backupPath,
                (int)swSaveAsVersion_e.swSaveAsCurrentVersion,
                (int)swSaveAsOptions_e.swSaveAsOptions_Copy,
                null, null,
                ref errors, ref warnings);

            if (!saved)
                throw new InvalidOperationException("Failed to create backup.");

            return backupPath;
        }

        private void replacePart_Click(object sender, EventArgs e)
        {
            ModelDoc2 model = SwApp.ActiveDoc;

            if (model == null ||
                model.GetType() != (int)swDocumentTypes_e.swDocASSEMBLY)
            {
                MessageBox.Show("Open an assembly first.");
                return;
            }

            AssemblyDoc assembly = (AssemblyDoc)model;
            SelectionMgr selMgr = (SelectionMgr)model.SelectionManager;

            if (selMgr.GetSelectedObjectCount2(-1) != 1 ||
                selMgr.GetSelectedObjectType3(1, -1) !=
                (int)swSelectType_e.swSelCOMPONENTS)
            {
                MessageBox.Show("Select exactly one component.");
                return;
            }

            Component2 component =
                (Component2)selMgr.GetSelectedObject6(1, -1);

            ModelDoc2 partDoc = component.GetModelDoc2();

            if (partDoc == null ||
                partDoc.GetType() != (int)swDocumentTypes_e.swDocPART)
            {
                MessageBox.Show("Selected component must be resolved.");
                return;
            }

            Part replacement = (Part)treeListView1.SelectedObject;

            if (replacement == null)
            {
                MessageBox.Show("Select a replacement part.");
                return;
            }

            // ------------------------------------------------
            // Set up the loading form
            // ------------------------------------------------

            LoadingForm loadingForm = new LoadingForm();
            loadingForm.Show(this); // non-modal so we can keep driving progress from here
            loadingForm.SetProgress(0);
            loadingForm.SetLabel("Starting replace...");
            Application.DoEvents();

            Stopwatch stopwatch = Stopwatch.StartNew();

            try
            {
                // ------------------------------------------------
                // Save a backup of the assembly in case the user wants to revert or the operation doesn't work properly
                // ------------------------------------------------

                loadingForm.SetLabel("Backing up assembly...");
                loadingForm.SetProgress(5);
                Application.DoEvents();

                string backupPath;
                try
                {
                    backupPath = CreateBackup(model);
                }
                catch (Exception ex)
                {
                    MessageBox.Show("Could not create backup — aborting replace.\n" + ex.Message);
                    return;
                }

                _backupStack.Push(backupPath);
                revertButton.Enabled = true;

                loadingForm.SetProgress(15);
                Application.DoEvents();

                string replacementPath =
                    VariantManager.GetVariants(replacement)
                        .First()
                        .SldprtPath;

                //--------------------------------------------------
                // Remember whether the component was already fixed
                //--------------------------------------------------

                bool wasFixed = component.IsFixed();
                
                var otherComponents = GetAllComponents(model)
                    .Where(c => c != component)
                    .ToList();

                var originalFixedStates = otherComponents
                    .ToDictionary(c => c, c => c.IsFixed());

                foreach (var c in otherComponents)
                {
                    if (!c.IsFixed())
                    {
                        model.ClearSelection2(true);
                        c.Select4(false, null, false);
                        assembly.FixComponent();
                    }
                }


                //--------------------------------------------------
                // Capture mates
                //--------------------------------------------------

                loadingForm.SetLabel("Capturing mates...");
                Application.DoEvents();

                List<RecordedMate> mates = SmartReplace.CaptureMates(model, component);

                loadingForm.SetProgress(25);
                Application.DoEvents();

                //--------------------------------------------------
                // Replace component
                //--------------------------------------------------

                loadingForm.SetLabel("Replacing component...");
                Application.DoEvents();

                model.ClearSelection2(true);

                component.Select4(false, null, false);

                bool success =
                    assembly.ReplaceComponents2(
                        replacementPath,
                        "",
                        false,
                        (int)swReplaceComponentsConfiguration_e
                            .swReplaceComponentsConfiguration_MatchName,
                        true);

                if (!success)
                {
                    MessageBox.Show("ReplaceComponents2 failed.");
                    return;
                }

                loadingForm.SetProgress(35);
                Application.DoEvents();

                component.MakeVirtual2(true);
                component.Name2 = replacement.Sku;

                model.ClearSelection2(true);

                component.Select4(false, null, false);

                // Only fix it if it wasn't already fixed
                if (!wasFixed)
                {
                    assembly.FixComponent();
                }

                loadingForm.SetProgress(40);
                Application.DoEvents();

                //--------------------------------------------------
                // Recreate mates
                //--------------------------------------------------

                int repaired = 0;
                int total = mates.Count;

                // Mate recreation spans 40% -> 90% of the bar
                const int mateStartProgress = 40;
                const int mateEndProgress = 90;

                for (int i = 0; i < total; i++)
                {
                    RecordedMate mate = mates[i];

                    loadingForm.SetLabel($"Recreating mate {i + 1} of {total}...");

                    try
                    {
                        if (SmartReplace.RecreateMate(
                                assembly,
                                model,
                                component,
                                mate))
                        {
                            repaired++;
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine(ex);
                    }

                    int progress = total == 0
                        ? mateEndProgress
                        : mateStartProgress +
                          (int)((mateEndProgress - mateStartProgress) * (i + 1) / (double)total);

                    loadingForm.SetProgress(progress);

                    // Simple ETA based on average time per mate so far
                    if (i + 1 > 0)
                    {
                        double avgSecondsPerMate = stopwatch.Elapsed.TotalSeconds / (i + 1);
                        int remaining = total - (i + 1);
                        loadingForm.SetETA(TimeSpan.FromSeconds(avgSecondsPerMate * remaining));
                    }

                    Application.DoEvents();
                }

                loadingForm.SetLabel("Rebuilding...");
                loadingForm.SetProgress(95);
                Application.DoEvents();

                model.EditRebuild3();

                //--------------------------------------------------
                // Restore original fixed state
                //--------------------------------------------------

                if (!wasFixed)
                {
                    model.ClearSelection2(true);
                    component.Select4(false, null, false);
                    assembly.UnfixComponent();
                }
                
                foreach (var kvp in originalFixedStates)
                {
                    if (!kvp.Value) // wasn't fixed originally
                    {
                        model.ClearSelection2(true);
                        kvp.Key.Select4(false, null, false);
                        assembly.UnfixComponent();
                    }
                }

                loadingForm.SetLabel("Done.");
                loadingForm.SetProgress(100);
                Application.DoEvents();

                loadingForm.Close();
                
                MessageBox.Show(
                    $"Finished.\n\nRecreated {repaired} mates.", "Success", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            finally
            {
                loadingForm.Close();
                
                
            }
        }

        private IEnumerable<Component2> GetAllComponents(ModelDoc2 model)
        {
            AssemblyDoc assembly = model as AssemblyDoc;

            if (assembly == null)
                yield break;

            object[] comps = (object[])assembly.GetComponents(false);

            if (comps == null)
                yield break;

            foreach (Component2 comp in comps)
            {
                if (comp.IsSuppressed())
                    continue;

                yield return comp;
            }
        }

        private void revertButton_Click(object sender, EventArgs e)
        {
            if (_backupStack.Count == 0)
            {
                MessageBox.Show("Nothing to revert.");
                return;
            }

            ModelDoc2 model = SwApp.ActiveDoc;
            if (model == null) return;

            string originalPath = model.GetPathName();
            string docTitle = model.GetTitle();
            string backupPath = _backupStack.Pop();

            try
            {
                SwApp.CloseDoc(docTitle);

                File.Copy(backupPath, originalPath, overwrite: true);

                int errors = 0, warnings = 0;
                ModelDoc2 reopened = SwApp.OpenDoc6(
                    originalPath,
                    (int)swDocumentTypes_e.swDocASSEMBLY,
                    (int)swOpenDocOptions_e.swOpenDocOptions_Silent,
                    "", ref errors, ref warnings);

                if (reopened == null)
                    MessageBox.Show("Revert failed to reopen the document.");
            }
            finally
            {
                // backup for this level is no longer needed
                TryDeleteFile(backupPath);

                revertButton.Enabled = _backupStack.Count != 0;
            }
        }

        private static void TryDeleteFile(string path)
        {
            try
            {
                File.Delete(path);
            }
            catch
            {
                /* best effort */
            }
        }
    }
}