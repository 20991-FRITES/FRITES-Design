using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

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
