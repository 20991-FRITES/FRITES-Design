using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FRITES_Design
{
    public class PartNode
    {
        public string PartName { get; set; }
        public List<PartNode> Children { get; set; } = new List<PartNode>();
    }
}
