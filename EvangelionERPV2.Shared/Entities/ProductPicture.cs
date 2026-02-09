namespace EvangelionERPV2.Shared.Entities
{
    public class ProductPicture
    {
        public ProductPicture() { }


        public Product Product { get; set; } = null!;
        public string File { get; set; } = string.Empty;
    }
}
