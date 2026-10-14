namespace EvangelionERPV2.Shared.DTOs
{
    public sealed class UpdateProductRequestDTO : IProductFiscalFields
    {
        private Guid? _variantGroupId;
        private string? _variantLabel;
        private bool _variantGroupIdWasProvided;
        private bool _variantLabelWasProvided;

        public Guid Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public decimal DefaultValue { get; set; }

        /// <summary>
        /// Optional, and unlike the fiscal block below an omitted value keeps the stored cost
        /// instead of clearing it: a caller without products.cost.read never receives the cost in
        /// the first place, so echoing back a null would silently erase it on every edit they save.
        /// </summary>
        public decimal? CostPrice { get; set; }

        public decimal StorageQuantity { get; set; }
        public string UnitOfMeasure { get; set; } = string.Empty;
        public string? PictureAdress { get; set; }
        public bool IsExternal { get; set; }
        public bool IsService { get; set; }
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

        /// <summary>A leaf of Limen's category tree; a variant takes its family's. Null keeps the current one.</summary>
        public Guid? StorefrontCategoryId { get; set; }
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

        public string? Ncm { get; set; }
        public string? Cest { get; set; }
        public string? Cean { get; set; }
        public string? CfopIntra { get; set; }
        public string? CfopInter { get; set; }
        public byte? Origem { get; set; }
        public string? UnidadeTributavel { get; set; }
        public string? Cst { get; set; }
        public string? Csosn { get; set; }
        public decimal? AliquotaIcms { get; set; }

        /// <summary>pRedBC as a fraction; only ICMS CST 20 uses it.</summary>
        public decimal? ReducaoBaseIcms { get; set; }
        public string? CstPis { get; set; }
        public decimal? AliquotaPis { get; set; }
        public string? CstCofins { get; set; }
        public decimal? AliquotaCofins { get; set; }
        public string? CodigoBeneficioFiscal { get; set; }
    }
}
