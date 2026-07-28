namespace FRITES.Core
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
        public int CategoryId { get; set; }
        public string ProductPageLink { get; set; }
        public bool CommonlyUsed { get; set; } = false;
        public string Material { get; set; } = null;
        public string Finish { get; set; } = null;
    }
}
