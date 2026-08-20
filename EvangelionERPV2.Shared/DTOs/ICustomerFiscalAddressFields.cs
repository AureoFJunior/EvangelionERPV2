namespace EvangelionERPV2.Shared.DTOs
{
    /// <summary>
    /// The structured destinatário address, shared by the create and update request bodies.
    /// </summary>
    /// <remarks>
    /// Same reason <see cref="IProductFiscalFields"/> exists: the field rules live in one validator
    /// helper instead of being copied into <c>CreateCustomerRequestValidator</c> and
    /// <c>UpdateCustomerRequestValidator</c>, where the two copies would be free to drift.
    /// </remarks>
    public interface ICustomerFiscalAddressFields
    {
        string? Logradouro { get; }
        string? Numero { get; }
        string? Complemento { get; }
        string? Bairro { get; }
        string? CodigoMunicipioIbge { get; }
        string? Municipio { get; }
        string? Uf { get; }
        string? Cep { get; }
        string? InscricaoEstadual { get; }
    }
}
