using EvangelionERPV2.Shared.DTOs;
using EvangelionERPV2.Web.FluentValidator;

namespace EvangelionERPV2.Test.Security
{
    /// <summary>
    /// The structured destinatário address on Customer. enderDest needs xLgr, nro, xBairro, cMun,
    /// xMun, UF and CEP as separate fields, and Customer.Adress cannot supply any of them.
    /// </summary>
    /// <remarks>
    /// The rules are shape-only and permitted-when-null on purpose: most customers never receive
    /// an NFe, and making the address mandatory here would break ordinary customer registration.
    /// A note is refused for an incomplete customer at issuance, before a number is consumed.
    /// </remarks>
    public class CustomerFiscalAddressRequestTests
    {
        private static readonly CreateCustomerRequestValidator CreateValidator = new();
        private static readonly UpdateCustomerRequestValidator UpdateValidator = new();

        private static CreateCustomerRequestDTO ValidCreateRequest() => new()
        {
            Name = "Cliente",
            Email = "cliente@example.com",
            PhoneNumber = "11999999999",
            Adress = "Avenida Paulista, 1000"
        };

        private static UpdateCustomerRequestDTO ValidUpdateRequest() => new()
        {
            Id = Guid.NewGuid(),
            Name = "Cliente",
            Email = "cliente@example.com",
            PhoneNumber = "11999999999",
            Adress = "Avenida Paulista, 1000"
        };

        [Fact]
        public void CreateCustomer_WithNoFiscalAddress_IsValid()
        {
            Assert.True(CreateValidator.Validate(ValidCreateRequest()).IsValid);
        }

        [Fact]
        public void UpdateCustomer_WithNoFiscalAddress_IsValid()
        {
            Assert.True(UpdateValidator.Validate(ValidUpdateRequest()).IsValid);
        }

        [Fact]
        public void CreateCustomer_WithCompleteFiscalAddress_IsValid()
        {
            var request = ValidCreateRequest();
            request.Logradouro = "Avenida Paulista";
            request.Numero = "1000";
            request.Complemento = "Conjunto 42";
            request.Bairro = "Bela Vista";
            request.CodigoMunicipioIbge = "3550308";
            request.Municipio = "Sao Paulo";
            request.Uf = "SP";
            request.Cep = "01310100";
            request.InscricaoEstadual = "110042490114";

            var result = CreateValidator.Validate(request);

            Assert.True(result.IsValid, string.Join("; ", result.Errors.Select(e => e.ErrorMessage)));
        }

        // A masked CEP is what a form actually posts. NormalizeDocument strips the punctuation, so
        // "01310-100" and "01310100" are the same eight digits.
        [Theory]
        [InlineData("01310100")]
        [InlineData("01310-100")]
        public void CreateCustomer_AcceptsCepWithOrWithoutMask(string cep)
        {
            var request = ValidCreateRequest();
            request.Cep = cep;

            Assert.True(CreateValidator.Validate(request).IsValid);
        }

        [Theory]
        [InlineData("0131010")]
        [InlineData("013101000")]
        [InlineData("abcdefgh")]
        public void CreateCustomer_RejectsMalformedCep(string cep)
        {
            var request = ValidCreateRequest();
            request.Cep = cep;

            var result = CreateValidator.Validate(request);

            Assert.False(result.IsValid);
            Assert.Contains(result.Errors, e => e.PropertyName == nameof(request.Cep));
        }

        [Theory]
        [InlineData("355030")]
        [InlineData("35503088")]
        public void CreateCustomer_RejectsMalformedCodigoMunicipioIbge(string codigo)
        {
            var request = ValidCreateRequest();
            request.CodigoMunicipioIbge = codigo;

            var result = CreateValidator.Validate(request);

            Assert.False(result.IsValid);
            Assert.Contains(result.Errors, e => e.PropertyName == nameof(request.CodigoMunicipioIbge));
        }

        // The v1 allowlist matches the emitente profile's. MG is a real UF and a legal
        // destinatário; it is refused because nothing here has been exercised outside SP/RS/SC/PR.
        [Theory]
        [InlineData("XX")]
        [InlineData("SPP")]
        [InlineData("BR")]
        public void CreateCustomer_RejectsAUfThatIsNotABrazilianState(string uf)
        {
            var request = ValidCreateRequest();
            request.Uf = uf;

            var result = CreateValidator.Validate(request);

            Assert.False(result.IsValid);
            Assert.Contains(result.Errors, e => e.PropertyName == nameof(request.Uf));
        }

        // MG and AM are outside the SP/RS/SC/PR allowlist that gates the *emitente*. A destinatário
        // may live in any state — an interstate NFe is ordinary — so that allowlist must not leak
        // onto the customer record.
        [Theory]
        [InlineData("SP")]
        [InlineData("sp")]
        [InlineData(" rs ")]
        [InlineData("MG")]
        [InlineData("AM")]
        public void CreateCustomer_AcceptsAnyBrazilianUfRegardlessOfCasingOrPadding(string uf)
        {
            var request = ValidCreateRequest();
            request.Uf = uf;

            Assert.True(CreateValidator.Validate(request).IsValid);
        }

        [Fact]
        public void UpdateCustomer_AppliesTheSameFiscalAddressRules()
        {
            var request = ValidUpdateRequest();
            request.Cep = "123";
            request.Uf = "XX";

            var result = UpdateValidator.Validate(request);

            Assert.False(result.IsValid);
            Assert.Contains(result.Errors, e => e.PropertyName == nameof(request.Cep));
            Assert.Contains(result.Errors, e => e.PropertyName == nameof(request.Uf));
        }

        [Fact]
        public void CreateCustomer_RejectsOverlongFreeTextFields()
        {
            var request = ValidCreateRequest();
            request.Logradouro = new string('a', 61);
            request.Municipio = new string('b', 61);

            var result = CreateValidator.Validate(request);

            Assert.False(result.IsValid);
            Assert.Contains(result.Errors, e => e.PropertyName == nameof(request.Logradouro));
            Assert.Contains(result.Errors, e => e.PropertyName == nameof(request.Municipio));
        }
    }
}
