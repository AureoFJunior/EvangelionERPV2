using EvangelionERPV2.Shared.DTOs;
using System.Text.Json;

namespace EvangelionERPV2.Test.Security
{
    public class SupplierDtoPrivacyTests
    {
        /// <summary>
        /// A supplier's CNPJ/CPF is LGPD-protected: it may be stored and filtered on, but it must
        /// never leave the API in a response body. Mirrors CustomerControllerPrivacyTests.
        /// </summary>
        [Fact]
        public void SupplierDto_Document_IsNotSerializedInApiResponses()
        {
            var dto = new SupplierDTO
            {
                Id = Guid.NewGuid(),
                Name = "Supplier",
                PhoneNumber = "11999999999",
                Email = "supplier@example.com",
                Adress = "Av. Example, 123",
                Document = "12345678901",
                EnterpriseId = Guid.NewGuid()
            };

            var json = JsonSerializer.Serialize(dto);

            Assert.DoesNotContain("\"Document\"", json, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("\"Name\":\"Supplier\"", json, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("\"Email\":\"supplier@example.com\"", json, StringComparison.OrdinalIgnoreCase);
        }
    }
}
