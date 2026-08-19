namespace EvangelionERPV2.Shared.DTOs
{
    public sealed class UpdateProductRequestDTO : IProductFiscalFields
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public decimal DefaultValue { get; set; }
        public decimal StorageQuantity { get; set; }
        public string UnitOfMeasure { get; set; } = string.Empty;
        public string? PictureAdress { get; set; }
        public bool IsExternal { get; set; }
        public bool IsService { get; set; }
        public bool? IsActive { get; set; }

        // Fiscal (NFe/NFCe). Null means "fall back to EnterpriseFiscalSettings.Default*", so an
        // update that omits them clears the per-product override rather than silently keeping a
        // stale one — the same all-or-nothing shape the rest of this DTO already has.
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
        public string? CstPis { get; set; }
        public decimal? AliquotaPis { get; set; }
        public string? CstCofins { get; set; }
        public decimal? AliquotaCofins { get; set; }
        public string? CodigoBeneficioFiscal { get; set; }
    }
}
