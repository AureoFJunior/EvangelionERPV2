namespace EvangelionERPV2.Shared.DTOs
{
    public sealed class UpdateProductRequestDTO : IProductFiscalFields
    {
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

        /// <summary>pRedBC as a fraction; only ICMS CST 20 uses it.</summary>
        public decimal? ReducaoBaseIcms { get; set; }
        public string? CstPis { get; set; }
        public decimal? AliquotaPis { get; set; }
        public string? CstCofins { get; set; }
        public decimal? AliquotaCofins { get; set; }
        public string? CodigoBeneficioFiscal { get; set; }
    }
}
