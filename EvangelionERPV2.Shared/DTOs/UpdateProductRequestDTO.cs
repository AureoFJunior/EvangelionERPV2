namespace EvangelionERPV2.Shared.DTOs
{
    public sealed class UpdateProductRequestDTO
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public double DefaultValue { get; set; }
        public double StorageQuantity { get; set; }
        public string UnitOfMeasure { get; set; } = string.Empty;
        public bool IsExternal { get; set; }
        public bool IsService { get; set; }
        public string? PictureAdress { get; set; }
        public bool? IsActive { get; set; }
    }
}
