namespace EvangelionERPV2.Shared.DTOs
{
    public sealed class ProductDTO : BaseDTO
    {
        public string Name { get; set; }
        public string Description { get; set; }
        public double DefaultValue { get; set; }
        public double StorageQuantity { get; set; }
        public bool IsExternal { get; set; }
        public bool IsService { get; set; }
        public string PictureAdress { get; set; }
    }
}