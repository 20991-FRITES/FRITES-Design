using FRITES.Core;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace FRITES_Design
{
    static class VariantManager
    {
        public static List<PartVariant> GetVariants(Part part)
        {
            string partDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "FRITES Design",
                "Step",
                part.Sku);

            if (!Directory.Exists(partDir))
                return new List<PartVariant>();

            return Directory
                .EnumerateFiles(partDir, "*.sldprt", SearchOption.TopDirectoryOnly)
                .Where(file =>
                {
                    var name = Path.GetFileNameWithoutExtension(file);

                    return !name.StartsWith("~$") &&
                           !name.StartsWith("~");
                })
                .Select(file => new PartVariant
                {
                    Part = part,
                    Name = Path.GetFileNameWithoutExtension(file),
                    SldprtPath = file
                })
                .OrderBy(v => v.Name)
                .ToList();
        }
    }
}