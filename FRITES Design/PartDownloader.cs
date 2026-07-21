using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

namespace FRITES_Design
{
    public class PartDownloader
    {
        private static readonly HttpClient _httpClient = new HttpClient();

        public static async Task DownloadPart(SldWorks SwApp, Part part, IProgress<int> progress)
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

                        using (Stream stream = await _httpClient.GetStreamAsync(uri))
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

        private static Bitmap ResizeImage(Image image, int width, int height)
        {
            var bitmap = new Bitmap(width, height);

            using (var g = Graphics.FromImage(bitmap))
            {
                g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                g.CompositingQuality = System.Drawing.Drawing2D.CompositingQuality.HighQuality;
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.HighQuality;

                g.DrawImage(image, 0, 0, width, height);
            }

            return bitmap;
        }

        public static async Task<(string imagePath, string thumbPath)> DownloadImageAsync(string imageUrl, string sku)
        {
            string appData = System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData);

            string imageDir = Path.Combine(appData, "FRITES Design", "Images");
            string thumbDir = Path.Combine(imageDir, "Thumbs");

            Directory.CreateDirectory(imageDir);
            Directory.CreateDirectory(thumbDir);

            string extension = Path.GetExtension(new Uri(imageUrl).AbsolutePath);
            if (string.IsNullOrWhiteSpace(extension))
                extension = ".jpg";

            string imagePath = Path.Combine(imageDir, $"{sku}{extension}");
            string thumbPath = Path.Combine(thumbDir, $"{sku}{extension}");

            // Already downloaded
            if (File.Exists(imagePath) && File.Exists(thumbPath))
                return (imagePath, thumbPath);

            try
            {
                using (var stream = await _httpClient.GetStreamAsync(imageUrl))
                using (var original = Image.FromStream(stream))
                using (var resized = ResizeImage(original, 400, 400))
                using (var thumb = ResizeImage(original, 64, 64))
                {
                    resized.Save(imagePath);
                    thumb.Save(thumbPath);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to download image: {imageUrl}");
                Debug.WriteLine(ex);

                // Clean up partially written files
                try
                {
                    if (File.Exists(imagePath))
                        File.Delete(imagePath);

                    if (File.Exists(thumbPath))
                        File.Delete(thumbPath);
                }
                catch { }

                throw;
            }

            return (imagePath, thumbPath);
        }

    }
}
