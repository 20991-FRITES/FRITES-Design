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

            SwApp.DocumentVisible(false, (int)swDocumentTypes_e.swDocPART);
            SwApp.DocumentVisible(false, (int)swDocumentTypes_e.swDocASSEMBLY);

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

                SwApp.DocumentVisible(true, (int)swDocumentTypes_e.swDocPART);
                SwApp.DocumentVisible(true, (int)swDocumentTypes_e.swDocASSEMBLY);
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

            string replacementPath =
                VariantManager.GetVariants(replacement)
                    .First()
                    .SldprtPath;

            //--------------------------------------------------
            // Remember whether the component was already fixed
            //--------------------------------------------------

            bool wasFixed = component.IsFixed();

            //--------------------------------------------------
            // Capture mates
            //--------------------------------------------------

            List<RecordedMate> mates =
                CaptureMates(model, component);

            //--------------------------------------------------
            // Replace component
            //--------------------------------------------------

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

            model.ClearSelection2(true);

            component.Select4(false, null, false);

            // Only fix it if it wasn't already fixed
            if (!wasFixed)
            {
                assembly.FixComponent();
            }

            //--------------------------------------------------
            // Recreate mates
            //--------------------------------------------------

            int repaired = 0;

            foreach (RecordedMate mate in mates)
            {
                try
                {
                    if (RecreateMate(
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
            }

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

            MessageBox.Show(
                $"Finished.\n\nRecreated {repaired} mates.");
        }

        private Entity ResolveEntity(
            ModelDoc2 model,
            Component2 replacementComponent,
            RecordedMateEntity recorded)
        {
            if (recorded.IsReplacementEntity)
            {
                switch (recorded.GeometryType)
                {
                    case RecordedEntityType.Face:
                    {
                        Face2 face =
                            FindBestFace(
                                replacementComponent,
                                recorded.FaceSignature);

                        double[] box = (double[])face.GetBox();

                        Debug.WriteLine(
                            $"{box[3] - box[0]} x {box[4] - box[1]} x {box[5] - box[2]}");

                        return face as Entity;
                    }

                    case RecordedEntityType.Edge:
                    {
                        Edge edge =
                            FindBestEdge(
                                replacementComponent,
                                recorded.EdgeSignature);

                        return edge as Entity;
                    }

                    case RecordedEntityType.Vertex:
                    {
                        Vertex vertex =
                            FindBestVertex(
                                replacementComponent,
                                recorded.VertexSignature);

                        return vertex as Entity;
                    }

                    default:
                        return null;
                }
            }

            int errors;

            return model.Extension.GetObjectByPersistReference3(
                recorded.PersistReference,
                out errors) as Entity;
        }

        private bool DeleteMate(
            ModelDoc2 model,
            RecordedMate mate)
        {
            model.ClearSelection2(true);

            if (!mate.OriginalFeature.Select2(false, 0))
                return false;

            return model.Extension.DeleteSelection2(
                (int)swDeleteSelectionOptions_e.swDelete_Absorbed);
        }


        private double CompareVertices(
            VertexSignature a,
            VertexSignature b)
        {
            double dx = a.Point[0] - b.Point[0];
            double dy = a.Point[1] - b.Point[1];
            double dz = a.Point[2] - b.Point[2];

            double distance = Math.Sqrt(dx * dx + dy * dy + dz * dz);

            // Larger score = better
            return -distance;
        }


        private Vertex FindBestVertex(
            Component2 component,
            VertexSignature original)
        {
            Vertex best = null;
            double bestScore = double.MinValue;

            foreach (Body2 body in (object[])component.GetBodies3(
                         (int)swBodyType_e.swSolidBody,
                         out _))
            {
                object[] vertices = body.GetVertices() as object[];

                if (vertices == null)
                    continue;

                foreach (Vertex vertex in vertices)
                {
                    VertexSignature sig = BuildSignature(vertex);

                    double score =
                        CompareVertices(original, sig);

                    if (score > bestScore)
                    {
                        bestScore = score;
                        best = vertex;
                    }
                }
            }

            return best;
        }

        private double CompareEdges(
            EdgeSignature a,
            EdgeSignature b)
        {
            double score = 0;

            if (a.CurveType != b.CurveType)
                return double.MinValue;

            score -= Math.Abs(a.Length - b.Length) * 1000.0;

            double dx =
                a.MidPoint[0] - b.MidPoint[0];

            double dy =
                a.MidPoint[1] - b.MidPoint[1];

            double dz =
                a.MidPoint[2] - b.MidPoint[2];

            score -= Math.Sqrt(dx * dx + dy * dy + dz * dz);

            if (a.CurveType == swCurveTypes_e.LINE_TYPE)
            {
                score +=
                    100.0 *
                    Math.Abs(
                        Dot(a.Direction, b.Direction));
            }

            if (a.CurveType == swCurveTypes_e.CIRCLE_TYPE)
            {
                score -=
                    Math.Abs(a.Radius - b.Radius) * 1000.0;
            }

            return score;
        }

        private Edge FindBestEdge(
            Component2 component,
            EdgeSignature original)
        {
            Edge best = null;

            double bestScore =
                double.MinValue;

            foreach (Body2 body in (object[])component.GetBodies3(
                         (int)swBodyType_e.swSolidBody,
                         out _))
            {
                object[] edges =
                    body.GetEdges() as object[];

                if (edges == null)
                    continue;

                foreach (Edge edge in edges)
                {
                    EdgeSignature sig =
                        BuildSignature(edge);

                    double score =
                        CompareEdges(original, sig);

                    if (score > bestScore)
                    {
                        bestScore = score;
                        best = edge;
                    }
                }
            }

            return best;
        }

        private static double Dot(
            double[] a,
            double[] b)
        {
            return
                a[0] * b[0] +
                a[1] * b[1] +
                a[2] * b[2];
        }


        private bool RecreateMate(
            AssemblyDoc assembly,
            ModelDoc2 model,
            Component2 replacementComponent,
            RecordedMate mate)
        {
            Debug.WriteLine("========================================");
            Debug.WriteLine($"Recreating {mate.Type}");

            if (mate.Entities.Count != 2)
                return false;

            //----------------------------------------
            // Resolve entities
            //----------------------------------------

            Entity entity1 = ResolveEntity(
                model,
                replacementComponent,
                mate.Entities[0]);

            Entity entity2 = ResolveEntity(
                model,
                replacementComponent,
                mate.Entities[1]);

            if (entity1 == null || entity2 == null)
            {
                Debug.WriteLine("Failed to resolve entities.");
                return false;
            }

            //----------------------------------------
            // Delete original mate
            //----------------------------------------

            if (!DeleteMate(model, mate))
            {
                Debug.WriteLine("Failed to delete mate.");
                return false;
            }

            //----------------------------------------
            // Select entities
            //----------------------------------------

            model.ClearSelection2(true);

            bool s1 = entity1.Select4(false, null);
            bool s2 = entity2.Select4(true, null);

            Debug.WriteLine($"Select1 = {s1}");
            Debug.WriteLine($"Select2 = {s2}");

            if (!s1 || !s2)
                return false;

            //----------------------------------------
            // Create mate
            //----------------------------------------

            int errors = 0;
            Mate2 newMate = null;

            switch ((swMateType_e)mate.Type)
            {
                case swMateType_e.swMateCOINCIDENT:

                    newMate = assembly.AddMate5(
                        (int)swMateType_e.swMateCOINCIDENT,
                        (int)mate.Alignment,
                        false, // Flip
                        0.0,
                        0.0,
                        0.0,
                        0.0,
                        0.0,
                        0.0,
                        0.0,
                        0.0,
                        false,
                        false,
                        0,
                        out errors);

                    break;

                default:

                    Debug.WriteLine($"Unsupported mate type: {mate.Type}");
                    return false;
            }

            model.ClearSelection2(true);

            Debug.WriteLine($"AddMate5 returned {(newMate != null)}");
            Debug.WriteLine($"ErrorStatus = {errors}");

            if (newMate == null)
                return false;

            model.EditRebuild3();

            return true;
        }

        private FaceSignature BuildSignature(
            Component2 component,
            Face2 face)
        {
            if (face == null)
                return null;

            FaceSignature sig = new FaceSignature();

            //----------------------------------------
            // Component bounding box
            //----------------------------------------

            object[] bodies = (object[])component.GetBodies3(
                (int)swBodyType_e.swSolidBody,
                out _);

            if (bodies == null || bodies.Length == 0)
                return sig;

            double minX = double.MaxValue;
            double minY = double.MaxValue;
            double minZ = double.MaxValue;

            double maxX = double.MinValue;
            double maxY = double.MinValue;
            double maxZ = double.MinValue;

            foreach (Body2 body in bodies)
            {
                double[] box = (double[])body.GetBodyBox();

                minX = Math.Min(minX, box[0]);
                minY = Math.Min(minY, box[1]);
                minZ = Math.Min(minZ, box[2]);

                maxX = Math.Max(maxX, box[3]);
                maxY = Math.Max(maxY, box[4]);
                maxZ = Math.Max(maxZ, box[5]);
            }

            //----------------------------------------
            // Surface
            //----------------------------------------

            Surface surface = face.GetSurface();

            if (surface == null)
                return sig;

            sig.SurfaceType = (swSurfaceTypes_e)surface.Identity();

            switch (sig.SurfaceType)
            {
                case swSurfaceTypes_e.PLANE_TYPE:
                {
                    double[] plane = (double[])surface.PlaneParams;

// Normal
                    sig.Normal[0] = plane[0];
                    sig.Normal[1] = plane[1];
                    sig.Normal[2] = plane[2];

                    Normalize(sig.Normal);

// Point on plane
                    double px = plane[3];
                    double py = plane[4];
                    double pz = plane[5];

                    Normalize(sig.Normal);

                    double ax = Math.Abs(sig.Normal[0]);
                    double ay = Math.Abs(sig.Normal[1]);
                    double az = Math.Abs(sig.Normal[2]);

                    double[] box = (double[])face.GetBox();

                    double dx = box[3] - box[0];
                    double dy = box[4] - box[1];
                    double dz = box[5] - box[2];

                    // Ignore the thickness direction
                    List<double> lengths = new List<double>()
                    {
                        dx,
                        dy,
                        dz
                    };

                    lengths.Sort();

                    sig.Extent1 = lengths[1];
                    sig.Extent2 = lengths[2];

                    if (ax >= ay && ax >= az)
                    {
                        sig.PlaneOffset =
                            NormalizePlaneOffset(px, minX, maxX);
                    }
                    else if (ay >= az)
                    {
                        sig.PlaneOffset =
                            NormalizePlaneOffset(py, minY, maxY);
                    }
                    else
                    {
                        sig.PlaneOffset =
                            NormalizePlaneOffset(pz, minZ, maxZ);
                    }

                    Debug.WriteLine(
                        $"Normal = ({sig.Normal[0]:F3}, {sig.Normal[1]:F3}, {sig.Normal[2]:F3})");

                    Debug.WriteLine(
                        $"Plane point = ({px:F3}, {py:F3}, {pz:F3})");

                    Debug.WriteLine(
                        $"PlaneOffset = {sig.PlaneOffset:F3}");

                    break;
                }

                case swSurfaceTypes_e.CYLINDER_TYPE:
                {
                    double[] cyl = (double[])surface.CylinderParams;

                    if (cyl != null && cyl.Length >= 7)
                    {
                        sig.Axis[0] = cyl[3];
                        sig.Axis[1] = cyl[4];
                        sig.Axis[2] = cyl[5];

                        Normalize(sig.Axis);
                    }

                    break;
                }

                case swSurfaceTypes_e.CONE_TYPE:
                {
                    double[] cone = (double[])surface.ConeParams;

                    if (cone != null && cone.Length >= 6)
                    {
                        sig.Axis[0] = cone[3];
                        sig.Axis[1] = cone[4];
                        sig.Axis[2] = cone[5];

                        Normalize(sig.Axis);
                    }

                    break;
                }
            }

            return sig;
        }

        private VertexSignature BuildSignature(Vertex vertex)
        {
            VertexSignature sig = new VertexSignature();

            double[] pt = (double[])vertex.GetPoint();

            sig.Point[0] = pt[0];
            sig.Point[1] = pt[1];
            sig.Point[2] = pt[2];

            return sig;
        }

        private EdgeSignature BuildSignature(Edge edge)
        {
            EdgeSignature sig = new EdgeSignature();

            Curve curve = edge.GetCurve();
            double[] curveParams = (double[])edge.GetCurveParams2();

            double startParam = curveParams[6];
            double endParam = curveParams[7];

            sig.Length = curve.GetLength3(startParam, endParam);


            sig.CurveType = (swCurveTypes_e)curve.Identity();

            Vertex start = edge.GetStartVertex();

            if (start != null)
            {
                double[] p = (double[])start.GetPoint();

                Array.Copy(p, sig.Start, 3);
            }

            Vertex end = edge.GetEndVertex();

            if (end != null)
            {
                double[] p = (double[])end.GetPoint();

                Array.Copy(p, sig.End, 3);
            }

            sig.MidPoint[0] = (sig.Start[0] + sig.End[0]) * 0.5;
            sig.MidPoint[1] = (sig.Start[1] + sig.End[1]) * 0.5;
            sig.MidPoint[2] = (sig.Start[2] + sig.End[2]) * 0.5;

            switch (sig.CurveType)
            {
                case swCurveTypes_e.LINE_TYPE:
                {
                    sig.Direction[0] =
                        sig.End[0] - sig.Start[0];

                    sig.Direction[1] =
                        sig.End[1] - sig.Start[1];

                    sig.Direction[2] =
                        sig.End[2] - sig.Start[2];

                    Normalize(sig.Direction);

                    break;
                }

                case swCurveTypes_e.CIRCLE_TYPE:
                {
                    double[] circle =
                        (double[])curve.CircleParams;

                    sig.Center[0] = circle[0];
                    sig.Center[1] = circle[1];
                    sig.Center[2] = circle[2];

                    sig.Radius = circle[6];

                    break;
                }
            }

            return sig;
        }

        private static void Normalize(double[] v)
        {
            double len =
                Math.Sqrt(v[0] * v[0] +
                          v[1] * v[1] +
                          v[2] * v[2]);

            if (len < 1e-9)
                return;

            v[0] /= len;
            v[1] /= len;
            v[2] /= len;
        }

        private IEnumerable<Face2> GetFaces(Component2 component)
        {
            object[] bodies =
                (object[])component.GetBodies3(
                    (int)swBodyType_e.swSolidBody,
                    out _);

            if (bodies == null)
                yield break;

            foreach (Body2 body in bodies)
            {
                object[] faces =
                    (object[])body.GetFaces();

                if (faces == null)
                    continue;

                foreach (Face2 face in faces)
                    yield return face;
            }
        }

        private Face2 FindBestFace(
            Component2 component,
            FaceSignature original)
        {
            List<(Face2 Face, FaceSignature Sig)> candidates =
                GetFaces(component)
                    .Select(f => (f, BuildSignature(component, f)))
                    .ToList();

            Debug.WriteLine($"Initial: {candidates.Count}");

            //------------------------------------
            // Surface type
            //------------------------------------

            var filtered = candidates
                .Where(x => x.Sig.SurfaceType == original.SurfaceType)
                .ToList();

            Debug.WriteLine($"Surface: {filtered.Count}");

            if (filtered.Any())
                candidates = filtered;

            //------------------------------------
            // Plane orientation
            //------------------------------------

            if (original.SurfaceType == swSurfaceTypes_e.PLANE_TYPE)
            {
                filtered = candidates
                    .Where(x =>
                        Dot(x.Sig.Normal, original.Normal) > 0.99)
                    .ToList();

                Debug.WriteLine($"Normal: {filtered.Count}");

                if (filtered.Any())
                    candidates = filtered;

                //------------------------------------
                // Plane position
                //------------------------------------

                var ranked = candidates
                    .OrderBy(x =>
                        Math.Abs(x.Sig.PlaneOffset - original.PlaneOffset))
                    .ThenBy(x =>
                        Math.Abs(x.Sig.Extent1 - original.Extent1) +
                        Math.Abs(x.Sig.Extent2 - original.Extent2));


                foreach (var c in ranked.Take(20))
                {
                    Debug.WriteLine(
                        $"Offset={Math.Abs(c.Sig.PlaneOffset - original.PlaneOffset):F4}  " +
                        $"Ext={c.Sig.Extent1:F4} x {c.Sig.Extent2:F4}");
                }

                Debug.WriteLine($"Final candidates: {candidates.Count}");

                return ranked.First().Face;
            }

            //------------------------------------
            // Fallback
            //------------------------------------

            return candidates.FirstOrDefault().Face;
        }

        private static double NormalizePlaneOffset(
            double point,
            double min,
            double max)
        {
            double size = max - min;

            if (size < 1e-9)
                return 0;

            return (point - min) / size;
        }

        private static double CenterDifference(
            double[] a,
            double[] b)
        {
            return
                Math.Abs(a[0] - b[0]) +
                Math.Abs(a[1] - b[1]) +
                Math.Abs(a[2] - b[2]);
        }

        private static double Distance(double[] a, double[] b)
        {
            double dx = a[0] - b[0];
            double dy = a[1] - b[1];
            double dz = a[2] - b[2];

            return Math.Sqrt(dx * dx + dy * dy + dz * dz);
        }

        private List<RecordedMate> CaptureMates(
            ModelDoc2 model,
            Component2 targetComponent)
        {
            var recordedMates = new List<RecordedMate>();

            ModelDocExtension ext = model.Extension;

            Feature feature = model.FirstFeature();

            while (feature != null)
            {
                if (feature.GetTypeName2() != "MateGroup")
                {
                    feature = feature.GetNextFeature();
                    continue;
                }

                Feature mateFeature = feature.GetFirstSubFeature();

                while (mateFeature != null)
                {
                    IMate2 mate = mateFeature.GetSpecificFeature2() as IMate2;

                    if (mate != null)
                    {
                        RecordedMate recorded = new RecordedMate
                        {
                            OriginalFeature = mateFeature,

                            Type = (swMateType_e)mate.Type,
                            Alignment = (swMateAlign_e)mate.Alignment,

                            Flipped = mate.Flipped,
                            CanBeFlipped = mate.CanBeFlipped,

                            MaximumVariation = mate.MaximumVariation,
                            MinimumVariation = mate.MinimumVariation
                        };

                        bool referencesTarget = false;

                        int count = mate.GetMateEntityCount();

                        for (int i = 0; i < count; i++)
                        {
                            MateEntity2 mateEntity = mate.MateEntity(i);

                            if (mateEntity == null)
                                continue;

                            Entity entity = mateEntity.Reference as Entity;

                            if (entity == null)
                                continue;

                            RecordedMateEntity recordedEntity =
                                new RecordedMateEntity();

                            recordedEntity.Component =
                                mateEntity.ReferenceComponent;

                            recordedEntity.Entity = entity;

                            recordedEntity.IsReplacementEntity =
                                mateEntity.ReferenceComponent == targetComponent;

                            if (recordedEntity.IsReplacementEntity)
                            {
                                referencesTarget = true;

                                object specific = entity.GetSafeEntity();

                                if (specific is Face2 face)
                                {
                                    recordedEntity.GeometryType =
                                        RecordedEntityType.Face;

                                    recordedEntity.FaceSignature =
                                        BuildSignature(
                                            mateEntity.ReferenceComponent,
                                            face);
                                }
                                else if (specific is Edge edge)
                                {
                                    recordedEntity.GeometryType =
                                        RecordedEntityType.Edge;

                                    recordedEntity.EdgeSignature =
                                        BuildSignature(edge);
                                }
                                else if (specific is Vertex vertex)
                                {
                                    recordedEntity.GeometryType =
                                        RecordedEntityType.Vertex;

                                    recordedEntity.VertexSignature =
                                        BuildSignature(vertex);
                                }
                            }
                            else
                            {
                                recordedEntity.PersistReference =
                                    (byte[])ext.GetPersistReference3(entity);
                            }

                            recorded.Entities.Add(recordedEntity);
                        }

                        if (referencesTarget)
                        {
                            // Capture dimension for distance / angle mates
                            DisplayDimension disp =
                                mate.DisplayDimension2[0];

                            if (disp != null)
                            {
                                Dimension dim =
                                    (Dimension)disp.GetDimension();

                                double[] value =
                                    (double[])dim.GetSystemValue3(
                                        (int)swInConfigurationOpts_e.swThisConfiguration,
                                        null);

                                if (value != null && value.Length > 0)
                                    recorded.Dimension = value[0];
                            }

                            recordedMates.Add(recorded);
                        }
                    }

                    mateFeature = mateFeature.GetNextSubFeature();
                }

                feature = feature.GetNextFeature();
            }

            return recordedMates;
        }
    }
}