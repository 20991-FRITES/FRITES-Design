using SolidWorks.Interop.dsgnchk;
using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Diagnostics;
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
                image_link TEXT
            );";

            command.ExecuteNonQuery();

            connection.Close();
        }

        private const string PART_LIST_ENDPOINT =
    "https://gist.githubusercontent.com/Blue25GD/7732b771724a335f63114d55bbeab7ad/raw/a067eaf1e6a588eec047e0b2d2a00fded83892c8/parts";

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
                part.ImageLink = await DownloadImageAsync(part.ImageLink, part.Sku);
            }

            add_part(part);
        }

        private async Task<string> DownloadImageAsync(string imageUrl, string sku)
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string imageDir = Path.Combine(appData, "FRITES Design", "Images");

            Directory.CreateDirectory(imageDir);

            string extension = Path.GetExtension(new Uri(imageUrl).AbsolutePath);
            if (string.IsNullOrEmpty(extension))
                extension = ".jpg";

            string localPath = Path.Combine(imageDir, $"{sku}{extension}");

            if (File.Exists(localPath))
                return localPath;

            await imageLimiter.WaitAsync();

            try
            {
                // Another task may have downloaded it while we were waiting.
                if (File.Exists(localPath))
                    return localPath;

                byte[] data = await httpClient.GetByteArrayAsync(imageUrl);
                File.WriteAllBytes(localPath, data);
            }
            finally
            {
                imageLimiter.Release();
            }

            return localPath;
        }

        public List<Part> query_parts(string q, int max_results = 25)
        {
            var connection = get_conn();
            connection.Open();

            var command = connection.CreateCommand();

            command.CommandText = @"
            SELECT *
            FROM parts
            WHERE sku LIKE @q
            ORDER BY
                CASE
                    WHEN sku LIKE @prefix THEN 0
                    ELSE 1
                END,
                sku
            LIMIT @limit;
            ";

            command.Parameters.AddWithValue("@q", "%" + q + "%");
            command.Parameters.AddWithValue("@prefix", q + "%");

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
                        ImageLink = reader.IsDBNull(5) ? null : reader.GetString(5)
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
            INSERT OR IGNORE INTO parts (name, sku, step_link, manufacturer, image_link) VALUES (@name, @sku, @step_link, @manufacturer, @image_link)
            ";

            command.Parameters.AddWithValue("@name", part.Name);
            command.Parameters.AddWithValue("@sku", part.Sku);
            command.Parameters.AddWithValue("@step_link", part.StepLink);
            command.Parameters.AddWithValue("@manufacturer", part.Manufacturer);
            command.Parameters.AddWithValue("@image_link", part.ImageLink);

            command.ExecuteNonQuery();

            connection.Close();
        }
    }
}
