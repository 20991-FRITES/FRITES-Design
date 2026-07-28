using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using FRITES.Core;

namespace FRITES_Design
{
    public class DataManager
    {
        string DBPath;
        static readonly HttpClient httpClient = new HttpClient();

        private const string PART_LIST_ENDPOINT =
    "https://20991-frites.github.io/FRITES-Design-Scraper/full_structure.json";

        public DataManager()
        {
            DBPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "FRITES Design",
                "data.db");
        }

        private SQLiteConnection GetDBConnection()
        {
            return new SQLiteConnection($"Data Source={DBPath};");
        }

        public void SetupDB()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(DBPath));

            using (var connection = GetDBConnection())
            {
                connection.Open();

                using (var command = connection.CreateCommand())
                {
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
                        product_page_link TEXT,
                        commonly_used INTEGER DEFAULT 0,    
                        material TEXT,
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
                }
            }
        }

        public async Task UpdateParts(IProgress<int> progress = null)
        {
            string json = await httpClient.GetStringAsync(PART_LIST_ENDPOINT);

            JsonNode root = JsonNode.Parse(json);

            // Build category tree and collect all parts
            var parts = new List<Part>();
            ImportTree(root, null, parts);

            int total = parts.Count;
            int completed = 0;

            var semaphore = new SemaphoreSlim(16); // Increased concurrency for faster image downloads
            var tasks = new List<Task>();
            var partsBatch = new List<Part>();
            const int batchSize = 20;

            foreach (var part in parts)
            {
                await semaphore.WaitAsync();

                tasks.Add(Task.Run(async () =>
                {
                    try
                    {
                        await ProcessPartAsync(part);
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine(ex);
                    }
                    finally
                    {
                        lock (partsBatch)
                        {
                            partsBatch.Add(part);

                            if (partsBatch.Count >= batchSize)
                            {
                                var batch = new List<Part>(partsBatch);
                                partsBatch.Clear();
                                AddPartsBatch(batch);
                            }
                        }

                        int done = Interlocked.Increment(ref completed);
                        progress?.Report(done * 100 / total);

                        semaphore.Release();
                    }
                }));
            }

            await Task.WhenAll(tasks);

            // Insert remaining parts
            lock (partsBatch)
            {
                if (partsBatch.Count > 0)
                {
                    AddPartsBatch(partsBatch);
                }
            }
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

        private void ImportTree(
    JsonNode node,
    int? parentCategoryId,
    List<Part> parts)
        {
            int? currentCategoryId = parentCategoryId;

            if (node["sku"] == null)
            {
                currentCategoryId = AddCategoryFast(
                    node["title"]?.ToString() ?? "",
                    parentCategoryId);
            }
            else if (!DoesPartExist(node["sku"]?.ToString()))
            {
                parts.Add(new Part
                {
                    Name = node["title"]?.ToString() ?? "",
                    Sku = node["sku"]?.ToString(),
                    Manufacturer = "goBILDA",
                    StepLink = node["step_file"]?.ToString()
    ?? $"https://www.gobilda.com/content/step_files/{node["sku"]}.zip",
                    ImageLink = node["image_url"]?.ToString(),
                    CategoryId = currentCategoryId ?? 0,
                    ProductPageLink = node["url"]?.ToString(),
                    CommonlyUsed = node["commonly_used"]?.GetValue<bool>() ?? false,
                    Material = node["material"]?.ToString()
                });
            }

            if (node["children"] is JsonArray children)
            {
                foreach (var child in children)
                {
                    if (child != null)
                        ImportTree(child, currentCategoryId, parts);
                }
            }
        }

        private bool DoesPartExist(string v)
        {
            using (var connection = GetDBConnection())
            {
                connection.Open();
                using (var command = connection.CreateCommand())
                {
                    command.CommandText = @"
                    SELECT COUNT(*)
                    FROM parts
                    WHERE sku = @sku;";
                    command.Parameters.AddWithValue("@sku", v);
                    return Convert.ToInt32(command.ExecuteScalar()) > 0;
                }
            }
        }

        private async Task ProcessPartAsync(Part part)
        {
            Console.WriteLine(part.Name);
            Console.WriteLine($"SKU: {part.Sku}");

            if (!string.IsNullOrWhiteSpace(part.ImageLink))
            {
                var (imagePath, thumbPath) = await PartDownloader.DownloadImageAsync(part.ImageLink, part.Sku);
                part.ImageLink = imagePath;
                part.ThumbnailLink = thumbPath;
            }
        }



        public List<Part> QueryParts(string q, int max_results = 25)
        {
            var parts = new List<Part>();

            using (var connection = GetDBConnection())
            {
                connection.Open();

                using (var command = connection.CreateCommand())
                {
                    var words = q.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);

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

                    using (var reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            parts.Add(MapPart(reader));
                        }
                    }
                }
            }

            return parts;
        }

        private void AddPartsBatch(List<Part> parts)
        {
            if (parts == null || parts.Count == 0)
                return;

            using (var connection = GetDBConnection())
            {
                connection.Open();

                // Optimize SQLite for bulk inserts
                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "PRAGMA synchronous = OFF";
                    command.ExecuteNonQuery();

                    command.CommandText = "PRAGMA journal_mode = WAL";
                    command.ExecuteNonQuery();
                }

                using (var transaction = connection.BeginTransaction())
                {
                    using (var command = connection.CreateCommand())
                    {
                        command.Transaction = transaction;

                        foreach (var part in parts)
                        {
                            command.CommandText = @"INSERT OR IGNORE INTO parts
                            (name, sku, step_link, manufacturer, image_link, thumbnail_link, category_id, product_page_link, commonly_used, material)
                            VALUES
                            (@name,@sku,@step_link,@manufacturer,@image_link,@thumbnail_link,@category_id,@product_page_link,@commonly_used,@material)";

                            command.Parameters.Clear();
                            command.Parameters.AddWithValue("@name", part.Name ?? "");
                            command.Parameters.AddWithValue("@sku", part.Sku ?? "");
                            command.Parameters.AddWithValue("@step_link", part.StepLink ?? "");
                            command.Parameters.AddWithValue("@manufacturer", part.Manufacturer ?? "");
                            command.Parameters.AddWithValue("@image_link", part.ImageLink ?? (object)DBNull.Value);
                            command.Parameters.AddWithValue("@thumbnail_link", part.ThumbnailLink ?? (object)DBNull.Value);
                            command.Parameters.AddWithValue("@category_id", part.CategoryId);
                            command.Parameters.AddWithValue("@product_page_link", part.ProductPageLink ?? (object)DBNull.Value);
                            command.Parameters.AddWithValue("@commonly_used", part.CommonlyUsed);
                            command.Parameters.AddWithValue("@material", part.Material ?? (object)DBNull.Value);


                            command.ExecuteNonQuery();
                        }
                    }

                    transaction.Commit();
                }

                // Re-enable synchronous mode for normal operations
                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "PRAGMA synchronous = NORMAL";
                    command.ExecuteNonQuery();
                }
            }
        }

        private int AddCategoryFast(string name, int? parentId)
        {
            using (var connection = GetDBConnection())
            {
                connection.Open();

                using (var command = connection.CreateCommand())
                {
                    if (parentId.HasValue)
                    {
                        command.CommandText = "SELECT Id FROM categories WHERE name = @name AND parent_id = @parent LIMIT 1;";
                        command.Parameters.AddWithValue("@parent", parentId.Value);
                    }
                    else
                    {
                        command.CommandText = "SELECT Id FROM categories WHERE name = @name AND parent_id IS NULL LIMIT 1;";
                        command.Parameters.AddWithValue("@parent", DBNull.Value);
                    }
                    command.Parameters.AddWithValue("@name", name ?? "");

                    var existingId = command.ExecuteScalar();
                    if (existingId != null)
                    {
                        return Convert.ToInt32(existingId);
                    }

                    command.CommandText = @"
                    INSERT INTO categories(name, parent_id)
                    VALUES(@name, @parent);

                    SELECT last_insert_rowid();";

                    return Convert.ToInt32(command.ExecuteScalar());
                }
            }
        }

        public List<Category> GetRootCategories()
        {
            List<Category> categories = new List<Category>();

            using (var connection = GetDBConnection())
            {
                connection.Open();

                using (var command = connection.CreateCommand())
                {
                    //command.CommandText = @"
                    //    SELECT Id, name, parent_id
                    //    FROM categories
                    //    WHERE parent_id IS (SELECT id FROM categories WHERE parent_id IS NULL)
                    //    ORDER BY name;";

                    command.CommandText = @"
                        SELECT c1.Id AS Id, c1.name as name, c1.parent_id as parent_id
                        FROM categories AS c1
                        INNER JOIN categories AS c2 ON c1.parent_id = c2.Id AND c2.parent_id IS NULL";

                    using (var reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            categories.Add(MapCategory(reader));
                        }
                    }
                }
            }

            return categories;
        }

        public List<Category> GetChildCategories(int parentId)
        {
            List<Category> categories = new List<Category>();

            using (var connection = GetDBConnection())
            {
                connection.Open();

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = @"
                        SELECT Id, name, parent_id
                        FROM categories
                        WHERE parent_id = @parentId
                        ORDER BY name;";

                    command.Parameters.AddWithValue("@parentId", parentId);

                    using (var reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            categories.Add(MapCategory(reader));
                        }
                    }
                }
            }

            return categories;
        }

        public List<Part> GetParts(int categoryId)
        {
            List<Part> parts = new List<Part>();

            using (var connection = GetDBConnection())
            {
                connection.Open();

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = @"
                        SELECT *
                        FROM parts
                        WHERE category_id = @categoryId
                        ORDER BY sku;";

                    command.Parameters.AddWithValue("@categoryId", categoryId);

                    using (var reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            parts.Add(MapPart(reader));
                        }
                    }
                }
            }

            return parts;
        }

        public Category GetCategoryById(int id)
        {
            using (var connection = GetDBConnection())
            {
                connection.Open();

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = @"
                        SELECT Id, name, parent_id
                        FROM categories
                        WHERE Id = @id;";

                    command.Parameters.AddWithValue("@id", id);
                    
                    using (var reader = command.ExecuteReader())
                    {
                        if (!reader.Read())
                            return null;

                        return MapCategory(reader);
                    }
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
            using (var connection = GetDBConnection())
            {
                connection.Open();

                using (var command = connection.CreateCommand())
                {
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

        private Part MapPart(SQLiteDataReader reader)
        {
            return new Part
            {
                Id = reader.GetInt32(0),
                Name = reader.GetString(1),
                Sku = reader.GetString(2),
                StepLink = reader.GetString(3),
                Manufacturer = reader.IsDBNull(4) ? null : reader.GetString(4),
                ImageLink = reader.IsDBNull(5) ? null : reader.GetString(5),
                ThumbnailLink = reader.IsDBNull(6) ? null : reader.GetString(6),
                CategoryId = reader.GetInt32(7),
                ProductPageLink = reader.IsDBNull(8) ? null : reader.GetString(8),
                CommonlyUsed = reader.GetInt32(9) == 1,
                Material = reader.IsDBNull(10) ? null : reader.GetString(10)
            };
        }

        private Category MapCategory(SQLiteDataReader reader)
        {
            return new Category
            {
                Id = reader.GetInt32(0),
                Name = reader.GetString(1),
                ParentId = reader.IsDBNull(2) ? (int?)null : reader.GetInt32(2)
            };
        }

        internal List<Part> GetCommonlyUsedParts()
        {
            List<Part> parts = new List<Part>();

            using (var connection = GetDBConnection())
            {
                connection.Open();

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = @"
                SELECT *
                FROM parts
                WHERE commonly_used = 1
                ORDER BY id;";

                    using (var reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            parts.Add(MapPart(reader));
                        }
                    }
                }
            }

            return parts;
        }
    }
}
