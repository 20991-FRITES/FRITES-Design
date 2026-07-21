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
        public int CategoryId { get; set; }
    }
}
