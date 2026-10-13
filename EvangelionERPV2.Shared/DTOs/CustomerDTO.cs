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

        public string? Logradouro { get; set; }
        public string? Numero { get; set; }
        public string? Complemento { get; set; }
        public string? Bairro { get; set; }
        public string? CodigoMunicipioIbge { get; set; }
        public string? Municipio { get; set; }
        public string? Uf { get; set; }
        public string? Cep { get; set; }
        public string? InscricaoEstadual { get; set; }

        /// <summary>How much this customer may owe at once; null means no limit.</summary>
        public decimal? CreditLimit { get; set; }
        public Guid? EnterpriseId { get; set; } = null;
    }
}
