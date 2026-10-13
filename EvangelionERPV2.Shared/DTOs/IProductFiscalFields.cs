namespace EvangelionERPV2.Shared.DTOs
{
    /// <summary>
    /// The per-product fiscal overrides, shared by the create and update request bodies.
    /// </summary>
    public interface IProductFiscalFields
    {
        string? Ncm { get; }
        string? Cest { get; }
        string? Cean { get; }
        string? CfopIntra { get; }
        string? CfopInter { get; }
        byte? Origem { get; }
        string? UnidadeTributavel { get; }
        string? Cst { get; }
        string? Csosn { get; }
        decimal? AliquotaIcms { get; }
        decimal? ReducaoBaseIcms { get; }
        string? CstPis { get; }
        decimal? AliquotaPis { get; }
        string? CstCofins { get; }
        decimal? AliquotaCofins { get; }
        string? CodigoBeneficioFiscal { get; }
    }
}
