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

        private const string PART_LIST_ENDPOINT =
    "https://gist.githubusercontent.com/Blue25GD/7732b771724a335f63114d55bbeab7ad/raw/96c0f7512330727260348eb07e64691d175d3a54/parts";

        private readonly SemaphoreSlim imageLimiter = new SemaphoreSlim(8); // Max simultaneous image downloads
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
                thumbnail_link TEXT,
                category_id INTEGER,
                FOREIGN KEY(category_id) REFERENCES categories(Id)
            );";



            command.ExecuteNonQuery();

            command.CommandText = @"CREATE INDEX IF NOT EXISTS idx_parts_sku
            ON parts(sku);";

            command.ExecuteNonQuery();

            command.CommandText = @"CREATE TABLE IF NOT EXISTS categories
            (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                name TEXT NOT NULL,
                parent_id INTEGER
            );";

            command.ExecuteNonQuery();
            connection.Close();
        }

        public async Task update_parts(IProgress<int> progress = null)
        {
            string json = await httpClient.GetStringAsync(PART_LIST_ENDPOINT);

            JsonNode root = JsonNode.Parse(json);

            int total = CountParts(root);
            int completed = 0;

            await ImportNode(root, null, total, progress, () =>
            {
                int done = Interlocked.Increment(ref completed);
                progress?.Report(done * 100 / total);
            });
        }

        private int CountParts(JsonNode node)
        {
            int count = node["sku"] != null ? 1 : 0;

            if (node["children"] is JsonArray children)
            {
                foreach (JsonNode child in children)
                {
                    if (child != null)
                        count += CountParts(child);
                }
            }

            return count;
        }

        private async Task ImportNode(
            JsonNode node,
            int? parentCategoryId,
            int total,
            IProgress<int> progress,
            Action partCompleted)
        {
            int? currentCategoryId = parentCategoryId;

            // Category
            if (node["sku"] == null)
            {
                currentCategoryId = add_category(
                    node["title"]?.ToString() ?? "",
                    parentCategoryId);
            }
            else
            {
                Part part = new Part
                {
                    Name = node["title"]?.ToString() ?? "",
                    Sku = node["sku"].ToString(),
                    Manufacturer = "goBILDA",
                    StepLink = $"https://www.gobilda.com/content/step_files/{node["sku"]}.zip",
                    ImageLink = node["image_url"]?.ToString(),
                    CategoryId = currentCategoryId ?? 0
                };

                try
                {
                    await ProcessPartAsync(part);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine(ex);

                    // Still insert the part without an image
                    add_part(part);
                }

                partCompleted();
            }

            if (node["children"] is JsonArray children)
            {
                foreach (JsonNode child in children)
                {
                    if (child != null)
                        await ImportNode(
                            child,
                            currentCategoryId,
                            total,
                            progress,
                            partCompleted);
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
                    try
                    {
                        byte[] data = await httpClient.GetByteArrayAsync(imageUrl);

                        var ms = new MemoryStream(data);
                        var original = Image.FromStream(ms);

                        var resized = ResizeImage(original, 400, 400);
                        resized.Save(imagePath);

                        var thumb = ResizeImage(original, 64, 64);
                        thumb.Save(thumbPath);
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"FAILED: {imageUrl}");
                        Debug.WriteLine(ex);

                        throw;
                    }
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
                        ThumbnailLink = reader.IsDBNull(6) ? null : reader.GetString(6),
                        CategoryId = reader.GetInt32(7),
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

            command.CommandText = @"INSERT OR IGNORE INTO parts
(name, sku, step_link, manufacturer, image_link, thumbnail_link, category_id)
VALUES
(@name,@sku,@step_link,@manufacturer,@image_link,@thumbnail_link,@category_id)
";

            command.Parameters.AddWithValue("@name", part.Name);
            command.Parameters.AddWithValue("@sku", part.Sku);
            command.Parameters.AddWithValue("@step_link", part.StepLink);
            command.Parameters.AddWithValue("@manufacturer", part.Manufacturer);
            command.Parameters.AddWithValue("@image_link", part.ImageLink);
            command.Parameters.AddWithValue("@thumbnail_link", part.ThumbnailLink);
            command.Parameters.AddWithValue("@category_id", part.CategoryId);

            command.ExecuteNonQuery();

            connection.Close();
        }

        private int add_category(string name, int? parentId)
        {
            var connection = get_conn();
            connection.Open();

            var command = connection.CreateCommand();

            command.CommandText = @"
        INSERT INTO categories(name, parent_id)
        VALUES(@name, @parent);

        SELECT last_insert_rowid();
    ";

            command.Parameters.AddWithValue("@name", name);
            command.Parameters.AddWithValue("@parent", (object)parentId ?? DBNull.Value);

            return Convert.ToInt32(command.ExecuteScalar());
        }

        public List<Category> GetRootCategories()
        {
            var connection = get_conn();
            connection.Open();

            var command = connection.CreateCommand();

            command.CommandText = @"
                SELECT Id, name, parent_id
                FROM categories
                WHERE parent_id IS NULL
                ORDER BY name;";

            List<Category> categories = new List<Category>();

            using (var reader = command.ExecuteReader())
            {
                while (reader.Read())
                {
                    categories.Add(new Category
                    {
                        Id = reader.GetInt32(0),
                        Name = reader.GetString(1),
                        ParentId = reader.IsDBNull(2) ? (int?)null : reader.GetInt32(2)
                    });
                }
            }

            return categories;
        }

        public List<Category> GetChildCategories(int parentId)
        {
            var connection = get_conn();
            connection.Open();

            var command = connection.CreateCommand();

            command.CommandText = @"
        SELECT Id, name, parent_id
        FROM categories
        WHERE parent_id = @parentId
        ORDER BY name;";

            command.Parameters.AddWithValue("@parentId", parentId);

            List<Category> categories = new List<Category>();

            using (var reader = command.ExecuteReader())
            {
                while (reader.Read())
                {
                    categories.Add(new Category
                    {
                        Id = reader.GetInt32(0),
                        Name = reader.GetString(1),
                        ParentId = reader.GetInt32(2)
                    });
                }
            }

            return categories;
        }

        public List<Part> GetParts(int categoryId)
        {
            var connection = get_conn();
            connection.Open();

            var command = connection.CreateCommand();

            command.CommandText = @"
        SELECT *
        FROM parts
        WHERE category_id = @categoryId
        ORDER BY sku;";

            command.Parameters.AddWithValue("@categoryId", categoryId);

            List<Part> parts = new List<Part>();

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
                        ThumbnailLink = reader.IsDBNull(6) ? null : reader.GetString(6),
                        CategoryId = reader.GetInt32(7)
                    });
                }
            }

            return parts;
        }

        public Category GetCategoryById(int id)
        {
            using (var connection = get_conn())
            {
                connection.Open();

                var command = connection.CreateCommand();

                command.CommandText = @"
            SELECT Id, name, parent_id
            FROM categories
            WHERE Id = @id;";

                command.Parameters.AddWithValue("@id", id);

                using (var reader = command.ExecuteReader())
                {
                    if (!reader.Read())
                        return null;

                    return new Category
                    {
                        Id = reader.GetInt32(0),
                        Name = reader.GetString(1),
                        ParentId = reader.IsDBNull(2) ? (int?)null : reader.GetInt32(2)
                    };
                }
            }
        }

        public Category GetParent(Category category)
        {
            if (category.ParentId == null)
                return null;

            return GetCategoryById(category.ParentId.Value);
        }



        public bool DoesCategoryHaveChildren(int categoryId)
        {
            using (var connection = get_conn())
            {
                connection.Open();

                var command = connection.CreateCommand();

                command.CommandText = @"
            SELECT
                EXISTS(
                    SELECT 1
                    FROM categories
                    WHERE parent_id = @categoryId
                )
                OR
                EXISTS(
                    SELECT 1
                    FROM parts
                    WHERE category_id = @categoryId
                );";

                command.Parameters.AddWithValue("@categoryId", categoryId);

                return Convert.ToBoolean(command.ExecuteScalar());
            }
        }
    }
}
