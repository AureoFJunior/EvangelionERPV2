using EvangelionERPV2.Shared.Enums;

namespace EvangelionERPV2.Shared.DTOs
{
    public sealed class ProductDTO : BaseDTO
    {
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public decimal DefaultValue { get; set; }

        // Cost and margin are Manager-tier: nulled by ProductController for a caller without
        // products.cost.read, the same withholding shape EmployeeDTO uses for salary.
        public decimal? CostPrice { get; set; }
        public decimal? MarginPercent { get; set; }

        public decimal StorageQuantity { get; set; }
        public string UnitOfMeasure { get; set; } = nameof(EnumUnitOfMeasure.Unit);
        public bool IsExternal { get; set; }
        public bool IsService { get; set; }
        public string PictureAdress { get; set; } = string.Empty;

        // Fiscal (NFe/NFCe). Null means "use the enterprise default" — see NFeItemFiscalResolver.
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
