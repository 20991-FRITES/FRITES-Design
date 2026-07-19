using SolidWorks.Interop.dsgnchk;
using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;


namespace FRITES_Design
{
    public class DataManager
    {
        string dbPath;
        static readonly HttpClient httpClient = new HttpClient();
        public DataManager()
        {
            dbPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "FRITES Design",
                "data.db");

        }

        private SQLiteConnection get_conn()
        {
            return new SQLiteConnection($"Data Source={dbPath};");
        }

        public void setup_db()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(dbPath));

            var connection = get_conn();
            connection.Open();

            var command = connection.CreateCommand();

            command.CommandText = @"
            CREATE TABLE IF NOT EXISTS parts
            (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                name TEXT NOT NULL,
                sku TEXT NOT NULL,
                step_link TEXT NOT NULL,
                manufacturer TEXT,
                image_link TEXT,
                thumbnail_link TEXT
            );";



            command.ExecuteNonQuery();

            command.CommandText = @"CREATE INDEX IF NOT EXISTS idx_parts_sku
ON parts(sku);";

            command.ExecuteNonQuery();

            connection.Close();
        }

        private const string PART_LIST_ENDPOINT =
    "https://gist.githubusercontent.com/Blue25GD/7732b771724a335f63114d55bbeab7ad/raw/121e7f709941566756145320f710ee62564967ff/parts";

        private readonly SemaphoreSlim imageLimiter = new SemaphoreSlim(8); // Max simultaneous image downloads

        public async Task update_parts(IProgress<int> progress = null)
        {
            string json = await httpClient.GetStringAsync(PART_LIST_ENDPOINT);

            JsonNode root = JsonNode.Parse(json);

            List<Part> parts = EnumerateParts(root).ToList();

            int total = parts.Count;
            int completed = 0;



            var tasks = parts.Select(async part =>
            {
                await ProcessPartAsync(part);

                int done = Interlocked.Increment(ref completed);
                progress?.Report(done * 100 / total);
            });

            await Task.WhenAll(tasks);


        }

        private IEnumerable<Part> EnumerateParts(JsonNode root)
        {
            Stack<JsonNode> stack = new Stack<JsonNode>();
            stack.Push(root);

            while (stack.Count > 0)
            {
                JsonNode node = stack.Pop();

                if (node["sku"] != null)
                {
                    yield return new Part
                    {
                        Name = node["title"]?.ToString() ?? "",
                        Sku = node["sku"].ToString(),
                        Manufacturer = "goBILDA",
                        StepLink = $"https://www.gobilda.com/content/step_files/{node["sku"]}.zip",
                        ImageLink = node["image_url"]?.ToString()
                    };
                }

                if (node["children"] is JsonArray children)
                {
                    foreach (JsonNode child in children)
                    {
                        if (child != null)
                            stack.Push(child);
                    }
                }
            }
        }

        private async Task ProcessPartAsync(Part part)
        {
            Console.WriteLine(part.Name);
            Console.WriteLine($"SKU: {part.Sku}");

            if (!string.IsNullOrWhiteSpace(part.ImageLink))
            {
                var (imagePath, thumbPath) = await DownloadImageAsync(part.ImageLink, part.Sku);
                part.ImageLink = imagePath;
                part.ThumbnailLink = thumbPath;
            }

            add_part(part);
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

        private async Task<(string, string)> DownloadImageAsync(string imageUrl, string sku)
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

            string imageDir = Path.Combine(appData, "FRITES Design", "Images");
            string thumbDir = Path.Combine(imageDir, "Thumbs");

            Directory.CreateDirectory(imageDir);
            Directory.CreateDirectory(thumbDir);

            string extension = Path.GetExtension(new Uri(imageUrl).AbsolutePath);
            if (string.IsNullOrEmpty(extension))
                extension = ".jpg";

            string imagePath = Path.Combine(imageDir, $"{sku}{extension}");
            string thumbPath = Path.Combine(thumbDir, $"{sku}{extension}");

            if (File.Exists(imagePath) && File.Exists(thumbPath))
                return (imagePath, thumbPath);

            await imageLimiter.WaitAsync();

            try
            {
                if (!File.Exists(imagePath) || !File.Exists(thumbPath))
                {
                    byte[] data = await httpClient.GetByteArrayAsync(imageUrl);

                    var ms = new MemoryStream(data);
                    var original = Image.FromStream(ms);

                    var resized = ResizeImage(original, 400, 400);
                    resized.Save(imagePath);

                    var thumb = ResizeImage(original, 64, 64);
                    thumb.Save(thumbPath);
                }
            }
            finally
            {
                imageLimiter.Release();
            }

            return (imagePath, thumbPath);
        }

        public List<Part> query_parts(string q, int max_results = 25)
        {
            var connection = get_conn();
            connection.Open();

            var command = connection.CreateCommand();

            var words = q
                .Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            
                        var sb = new StringBuilder(@"
            SELECT *
            FROM parts");
            
                        if (words.Length > 0)
                        {
                            sb.Append("\nWHERE ");
            
                            for (int i = 0; i < words.Length; i++)
                            {
                                if (i > 0)
                                    sb.Append(" AND ");
            
                                sb.Append($"(sku LIKE @w{i} OR name LIKE @w{i})");
                            }
                        }
            
                        sb.Append(@"
            ORDER BY sku
            LIMIT @limit;");

            command.CommandText = sb.ToString();

            for (int i = 0; i < words.Length; i++)
            {
                command.Parameters.AddWithValue($"@w{i}", "%" + words[i] + "%");
            }

            command.Parameters.AddWithValue("@limit", max_results);

            var parts = new List<Part>();

            using (var reader = command.ExecuteReader())
            {
                while (reader.Read())
                {
                    parts.Add(new Part
                    {
                        Id = reader.GetInt32(0),
                        Name = reader.GetString(1),
                        Sku = reader.GetString(2),
                        StepLink = reader.GetString(3),
                        Manufacturer = reader.IsDBNull(4) ? null : reader.GetString(4),
                        ImageLink = reader.IsDBNull(5) ? null : reader.GetString(5),
                        ThumbnailLink = reader.IsDBNull(6) ? null : reader.GetString(6)
                    });
                }
            }

            connection.Close();

            return parts;
        }

        private void add_part(Part part)
        {
            var connection = get_conn();
            connection.Open();

            var command = connection.CreateCommand();

            command.CommandText = @"
            INSERT OR IGNORE INTO parts (name, sku, step_link, manufacturer, image_link, thumbnail_link) VALUES (@name, @sku, @step_link, @manufacturer, @image_link, @thumbnail_link)
            ";

            command.Parameters.AddWithValue("@name", part.Name);
            command.Parameters.AddWithValue("@sku", part.Sku);
            command.Parameters.AddWithValue("@step_link", part.StepLink);
            command.Parameters.AddWithValue("@manufacturer", part.Manufacturer);
            command.Parameters.AddWithValue("@image_link", part.ImageLink);
            command.Parameters.AddWithValue("@thumbnail_link", part.ThumbnailLink);

            command.ExecuteNonQuery();

            connection.Close();
        }
    }
}
