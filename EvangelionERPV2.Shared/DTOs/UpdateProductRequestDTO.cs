namespace EvangelionERPV2.Shared.DTOs
{
    public sealed class UpdateProductRequestDTO
    {
        private Guid? _variantGroupId;
        private string? _variantLabel;
        private bool _variantGroupIdWasProvided;
        private bool _variantLabelWasProvided;

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

        public Guid? VariantGroupId
        {
            get => _variantGroupId;
            set
            {
                _variantGroupId = value;
                _variantGroupIdWasProvided = true;
            }
        }

        public string? VariantLabel
        {
            get => _variantLabel;
            set
            {
                _variantLabel = value;
                _variantLabelWasProvided = true;
            }
        }

        [System.Text.Json.Serialization.JsonIgnore]
        public bool VariantGroupingWasProvided => _variantGroupIdWasProvided || _variantLabelWasProvided;
    }
}
