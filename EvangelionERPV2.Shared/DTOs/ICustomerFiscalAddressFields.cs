namespace EvangelionERPV2.Shared.DTOs
{
    /// <summary>
    /// The structured destinatário address, shared by the create and update request bodies.
    /// </summary>
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
