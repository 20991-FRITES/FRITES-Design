using System.Collections.Generic;

namespace FRITES_Design
{
    public class PartNode
    {
        public string PartName { get; set; }
        public List<PartNode> Children { get; set; } = new List<PartNode>();
    }
}
