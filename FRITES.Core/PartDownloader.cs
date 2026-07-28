using SolidWorks.Interop.dsgnchk;
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
using System.Threading;
using System.Threading.Tasks;

namespace FRITES.Core
{
    public class PartDownloader
    {
        private const int DownloadTimeoutSeconds = 180;
        private const int DownloadRetryCount = 3;
        private static readonly HttpClient _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(DownloadTimeoutSeconds)
        };

        public static async Task<string> DownloadStepAsync(Part part)
        {
            string appData = System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData);

            string stepDir = Path.Combine(appData, "FRITES Design", "Step");
            Directory.CreateDirectory(stepDir);

            string partDirFinal = Path.Combine(stepDir, part.Sku);
            string partDirTemp = partDirFinal + ".tmp";
            Directory.CreateDirectory(partDirTemp);

            string localPartPath = Path.Combine(partDirTemp, part.Sku + ".sldprt");

            // Already imported previously.
            if (File.Exists(localPartPath))
                return string.Empty;

            Uri uri = new Uri(part.StepLink);

            string extension = Path.GetExtension(uri.LocalPath);
            string downloadPath = Path.Combine(
                partDirTemp,
                Guid.NewGuid().ToString() + extension);

            try
            {
                bool downloaded = await TryDownloadFileAsync(uri, downloadPath);

                if (!downloaded)
                    return null;

                extension = Path.GetExtension(downloadPath).ToLowerInvariant();

                if (extension == ".zip")
                {
                    ExtractZipRecursive(downloadPath, partDirTemp);
                    return partDirTemp;
                }

                if (extension == ".step" || extension == ".stp")
                {
                    string stepPath = Path.Combine(partDirTemp, part.Sku + extension);

                    if (!string.Equals(downloadPath, stepPath, StringComparison.OrdinalIgnoreCase))
                        File.Move(downloadPath, stepPath);

                    return partDirTemp;
                }

                throw new InvalidOperationException($"Unsupported file type: {extension}");
            }
            finally
            {
                if (File.Exists(downloadPath))
                {
                    try
                    {
                        File.Delete(downloadPath);
                    }
                    catch
                    {
                    }
                }
            }
        }

        private static void ExtractZipRecursive(string zipPath, string destinationDirectory)
        {
            ZipFile.ExtractToDirectory(zipPath, destinationDirectory);

            try
            {
                File.Delete(zipPath);
            }
            catch
            {
                // Ignore cleanup failures
            }

            var nestedZips = Directory.GetFiles(destinationDirectory, "*.zip", SearchOption.AllDirectories);

            foreach (var nestedZip in nestedZips)
            {
                string extractDir = Path.Combine(
                    Path.GetDirectoryName(nestedZip),
                    Path.GetFileNameWithoutExtension(nestedZip));

                Directory.CreateDirectory(extractDir);

                ExtractZipRecursive(nestedZip, extractDir);
            }
        }

        public static IEnumerable<string> EnumerateStepFiles(string folder)
        {
            return Directory
                .EnumerateFiles(folder, "*.step", SearchOption.AllDirectories)
                .Concat(
                    Directory.EnumerateFiles(folder, "*.stp", SearchOption.AllDirectories));
        }

        private static readonly Dictionary<string, Dictionary<string, string>> AppearanceFixes =
    new Dictionary<string, Dictionary<string, string>>
        {
            {
                "5203-2402-0003",
                new Dictionary<string, string>
                {
                    //{ 0, "MotorBlack.p2m" },
                    //{ 1, "Steel.p2m" },
                    //{ 2, "Steel.p2m" },
                    //{ 15, "Aluminum.p2m" },
                    //{ 24, "MotorBlack.p2m" }
                    { "Boss-Extrude1[2]", "plastic\\high gloss\\yellow high gloss plastic.p2m" }
                }
            }
        };

        private static void FixPartAppearance(SldWorks swApp, PartDoc part, string sku)
        {
            Dictionary<string, string> fixes;
            if (!AppearanceFixes.TryGetValue(sku, out fixes))
                return;

            object[] bodyObjects = part.GetBodies2(
                (int)swBodyType_e.swSolidBody,
                false) as object[];

            if (bodyObjects == null || bodyObjects.Length == 0)
                return;

            foreach (var fix in fixes)
            {
                string bodyName = fix.Key;

                Body2 body = bodyObjects
                    .Cast<Body2>()
                    .FirstOrDefault(b => b != null && b.Name == bodyName);

                if (body == null)
                {
                    Debug.WriteLine($"Body '{bodyName}' not found.");
                    continue;
                }

                ApplyAppearance(
                    swApp,
                    fix.Value,
                    (ModelDoc2)part,
                    body);
            }
        }

        private static void ApplyAppearance(
    SldWorks swApp,
    string appearance,
    ModelDoc2 model,
    Body2 body)
        {
            string installDir = Path.GetDirectoryName(swApp.GetExecutablePath());

            string appearanceFile = Path.Combine(
                installDir,
                "SOLIDWORKS",
                "data",
                "graphics",
                "Materials",
                appearance);

            if (!File.Exists(appearanceFile))
                return;

            RenderMaterial renderMat = model.Extension.CreateRenderMaterial(appearanceFile);

            if (renderMat == null)
                return;

            object entity = body;

            if (!renderMat.AddEntity(entity))
                throw new Exception("Failed to add body to render material.");

            int materialId;
            if (!model.Extension.AddRenderMaterial(renderMat, out materialId))
                throw new Exception("Failed to apply render material.");

            model.GraphicsRedraw2();
        }

        public static string ImportStep(
    SldWorks swApp,
    string sku,
    string name,
    string stepFile,
    string outputFile,
    bool preload = false,
    string swMaterial = null,
    string swFinish = null)
        {
            swApp.SetUserPreferenceToggle(
                (int)swUserPreferenceToggle_e.swMultiCAD_Enable3DInterconnect,
                true);

            Directory.CreateDirectory(Path.GetDirectoryName(outputFile));

            if (File.Exists(outputFile))
                return outputFile;

            ImportStepData importData =
                (ImportStepData)swApp.GetImportFileData(stepFile);

            importData.MapConfigurationData = true;

            int loadErrors = 0;

            ModelDoc2 stepDoc = (ModelDoc2)swApp.LoadFile4(
                stepFile,
                "",
                null,
                ref loadErrors);

            if (stepDoc == null)
                throw new Exception($"Failed to load STEP file. Error {loadErrors}");

            try
            {
                CustomPropertyManager props =
                    stepDoc.Extension.CustomPropertyManager[""];

                props.Add3(
                    "Part Number",
                    (int)swCustomInfoType_e.swCustomInfoText,
                    sku,
                    (int)swCustomPropertyAddOption_e.swCustomPropertyReplaceValue);

                props.Add3(
                    "Description",
                    (int)swCustomInfoType_e.swCustomInfoText,
                    name,
                    (int)swCustomPropertyAddOption_e.swCustomPropertyReplaceValue);

                var part = (PartDoc)stepDoc;

                if (!string.IsNullOrWhiteSpace(swMaterial))
                {
                    try
                    {
                        string installDir =
                            Path.GetDirectoryName(swApp.GetExecutablePath());

                        string materialDb = Path.Combine(
                            installDir,
                            "lang",
                            "english",
                            "sldmaterials",
                            "SOLIDWORKS Materials.sldmat");

                        string appearanceFolder = Path.Combine(
                            installDir,
                            "SOLIDWORKS",
                            "data",
                            "graphics",
                            "Materials");

                        part.SetMaterialPropertyName2(
                            "",
                            materialDb,
                            swMaterial);

                        string appearanceFile = Path.Combine(
                            appearanceFolder,
                            swFinish);

                        if (File.Exists(appearanceFile))
                        {
                            RenderMaterial renderMat =
                                stepDoc.Extension.CreateRenderMaterial(appearanceFile);

                            if (renderMat != null)
                            {
                                renderMat.AddEntity(stepDoc);

                                int materialId;
                                stepDoc.Extension.AddRenderMaterial(
                                    renderMat,
                                    out materialId);

                                stepDoc.GraphicsRedraw2();
                                stepDoc.ForceRebuild3(false);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Failed to set material: {swMaterial}");
                        Debug.WriteLine(ex);
                    }
                }

                
                FixPartAppearance(swApp, part, sku);

                int saveErrors = 0;
                int saveWarnings = 0;

                bool saved = stepDoc.Extension.SaveAs(
                    outputFile,
                    (int)swSaveAsVersion_e.swSaveAsCurrentVersion,
                    (int)swSaveAsOptions_e.swSaveAsOptions_Silent,
                    null,
                    ref saveErrors,
                    ref saveWarnings);

                if (!saved)
                    throw new Exception($"Failed to save part. Error {saveErrors}");
            }
            finally
            {
                try
                {
                    swApp.CloseDoc(stepDoc.GetTitle());
                }
                catch (Exception ex)
                {
                    Debug.WriteLine(ex);
                }
            }

            if (!preload)
                return outputFile;

            int preloadErrors = 0;
            int preloadWarnings = 0;

            ModelDoc2 preloadDoc = swApp.OpenDoc6(
                outputFile,
                (int)swDocumentTypes_e.swDocPART,
                (int)swOpenDocOptions_e.swOpenDocOptions_Silent,
                "",
                ref preloadErrors,
                ref preloadWarnings);

            if (preloadDoc == null)
                throw new Exception($"Failed to preload part. Error {preloadErrors}");

            return outputFile;
        }

        private static async Task<bool> TryDownloadFileAsync(Uri uri, string destinationPath)
        {
            for (int attempt = 1; attempt <= DownloadRetryCount; attempt++)
            {
                try
                {
                    using (HttpResponseMessage response = await _httpClient.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead))
                    {
                        response.EnsureSuccessStatusCode();

                        using (Stream stream = await response.Content.ReadAsStreamAsync())
                        using (FileStream file = File.Create(destinationPath))
                        {
                            await stream.CopyToAsync(file);
                        }
                    }

                    return true;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Download attempt {attempt} failed for {uri}");
                    Debug.WriteLine(ex);

                    if (File.Exists(destinationPath))
                    {
                        try
                        {
                            File.Delete(destinationPath);
                        }
                        catch { }
                    }

                    if (attempt < DownloadRetryCount)
                        await Task.Delay(TimeSpan.FromSeconds(2));
                }
            }

            return false;
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

            if (File.Exists(imagePath) && File.Exists(thumbPath))
                return (imagePath, thumbPath);

            for (int attempt = 1; attempt <= DownloadRetryCount; attempt++)
            {
                try
                {
                    using (var response = await _httpClient.GetAsync(imageUrl, HttpCompletionOption.ResponseHeadersRead))
                    {
                        response.EnsureSuccessStatusCode();

                        using (var stream = await response.Content.ReadAsStreamAsync())
                        using (var original = Image.FromStream(stream))
                        using (var resized = ResizeImage(original, 400, 400))
                        using (var thumb = ResizeImage(original, 64, 64))
                        {
                            resized.Save(imagePath);
                            thumb.Save(thumbPath);
                        }
                    }

                    return (imagePath, thumbPath);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Failed to download image attempt {attempt}: {imageUrl}");
                    Debug.WriteLine(ex);

                    try
                    {
                        if (File.Exists(imagePath))
                            File.Delete(imagePath);

                        if (File.Exists(thumbPath))
                            File.Delete(thumbPath);
                    }
                    catch { }

                    if (attempt < DownloadRetryCount)
                        await Task.Delay(TimeSpan.FromSeconds(2));
                }
            }

            Debug.WriteLine($"Skipping image download after {DownloadRetryCount} attempts: {imageUrl}");
            return (string.Empty, string.Empty);
        }
    }
}
