namespace EvangelionERPV2.Shared.DTOs
{
    public sealed class CreateProductRequestDTO : IProductFiscalFields
    {
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public decimal? DefaultValue { get; set; }

        /// <summary>Optional. Null leaves the cost unknown; it is never defaulted to zero.</summary>
        public decimal? CostPrice { get; set; }

        public decimal? StorageQuantity { get; set; }
        public string UnitOfMeasure { get; set; } = string.Empty;
        public bool? IsExternal { get; set; }
        public bool? IsService { get; set; }
        public string? PictureAdress { get; set; }
        public string? File { get; set; }
        public LegacyCreateProductRequestDTO? Product { get; set; }

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

    public sealed class LegacyCreateProductRequestDTO
    {
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public decimal DefaultValue { get; set; }
        public decimal StorageQuantity { get; set; }
        public string UnitOfMeasure { get; set; } = string.Empty;
        public bool IsExternal { get; set; }
        public bool IsService { get; set; }
        public string? PictureAdress { get; set; }
    }
}
