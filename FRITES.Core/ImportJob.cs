using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace FRITES.Core
{
    public class ImportJob
    {
        public string Sku { get; set; }

        public string Name { get; set; }

        public string StepFile { get; set; }
        public string Material { get; set; } = null;
        public string Finish { get; set; } = null;

        public static List<ImportJob> LoadJobs(string filePath)
        {
            using (FileStream stream = File.OpenRead(filePath))
            {
                return JsonSerializer.Deserialize<List<ImportJob>>(stream)
                    ?? throw new InvalidOperationException("Failed to deserialize import jobs.");
            }
        }

        public static void SaveJobs(string filePath, IEnumerable<ImportJob> jobs)
        {
            var options = new JsonSerializerOptions
            {
                WriteIndented = true
            };

            string json = JsonSerializer.Serialize(jobs, options);

            File.WriteAllText(filePath, json);
        }
    }
}
