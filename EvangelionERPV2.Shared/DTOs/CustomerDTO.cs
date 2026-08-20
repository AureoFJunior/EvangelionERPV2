using System.Text.Json.Serialization;

namespace EvangelionERPV2.Shared.DTOs
{
    public sealed class CustomerDTO : BaseDTO
    {
        public string Name { get; set; } = string.Empty;
        public string PhoneNumber { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string Adress { get; set; } = string.Empty;
        [JsonIgnore]
        public string? Document { get; set; }

        // Structured destinatário address for NFe enderDest. All optional — only a customer who
        // receives a fiscal document needs them.
        public string? Logradouro { get; set; }
        public string? Numero { get; set; }
        public string? Complemento { get; set; }
        public string? Bairro { get; set; }
        public string? CodigoMunicipioIbge { get; set; }
        public string? Municipio { get; set; }
        public string? Uf { get; set; }
        public string? Cep { get; set; }
        public string? InscricaoEstadual { get; set; }
        public Guid? EnterpriseId { get; set; } = null;
    }
}
