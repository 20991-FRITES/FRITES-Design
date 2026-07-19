using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FRITES_Design
{
    public class Part
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public string Sku { get; set; }
        public string StepLink { get; set; }
        public string Manufacturer { get; set; }
        public string ImageLink { get; set; }
        public string ThumbnailLink { get; set; }  // 48x48
    }
}
