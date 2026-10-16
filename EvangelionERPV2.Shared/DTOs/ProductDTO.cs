using EvangelionERPV2.Shared.Enums;

namespace EvangelionERPV2.Shared.DTOs
{
    public sealed class ProductDTO : BaseDTO
    {
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public decimal DefaultValue { get; set; }
        public decimal? CostPrice { get; set; }
        public decimal? MarginPercent { get; set; }
        public decimal StorageQuantity { get; set; }
        public string UnitOfMeasure { get; set; } = nameof(EnumUnitOfMeasure.Unit);
        public bool IsExternal { get; set; }
        public bool IsService { get; set; }
        public string PictureAdress { get; set; } = string.Empty;
        public string? PictureError { get; set; }

        public bool HasPicture { get; set; }
        public bool FamilyHasPicture { get; set; }

        public Guid? VariantGroupId { get; set; }
        public string? VariantLabel { get; set; }

        public bool IsPublishedOnStorefront { get; set; }
        public Guid? StorefrontCategoryId { get; set; }
        public string? StorefrontCategoryPath { get; set; }
        public decimal? CompareAtPrice { get; set; }
        public DateTime? PromotionEndsAt { get; set; }

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
        public decimal? ReducaoBaseIcms { get; set; }
        public string? CstPis { get; set; }
        public decimal? AliquotaPis { get; set; }
        public string? CstCofins { get; set; }
        public decimal? AliquotaCofins { get; set; }
        public string? CodigoBeneficioFiscal { get; set; }
    }
}
