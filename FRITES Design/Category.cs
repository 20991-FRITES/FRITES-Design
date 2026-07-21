using System.Collections.Generic;

namespace FRITES_Design
{
    public class Category
    {
        public int Id { get; set; }
        public int? ParentId { get; set; }

        public string Name { get; set; }

        public List<Category> Categories { get; } = new List<Category>();
        public List<Part> Parts { get; } = new List<Part>();
        public bool IsLoaded { get; set; }

    }
}
