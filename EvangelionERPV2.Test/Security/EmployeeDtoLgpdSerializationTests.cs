using EvangelionERPV2.Shared.DTOs;
using System.Text.Json;

namespace EvangelionERPV2.Test.Security
{
    public class EmployeeDtoLgpdSerializationTests
    {
        [Fact]
        public void EmployeeDTO_NeverSerializesDocumentOrSalary()
        {
            var dto = new EmployeeDTO
            {
                Id = Guid.NewGuid(),
                Name = "Employee Test",
                Email = "employee@test.com",
                PhoneNumber = "11999999999",
                Position = "Analyst",
                Document = "12345678901",
                Salary = 4500.50m,
                HireDate = DateTime.UtcNow,
                EnterpriseId = Guid.NewGuid()
            };

            var json = JsonSerializer.Serialize(dto);

            Assert.DoesNotContain("12345678901", json);
            Assert.DoesNotContain("4500.5", json);
            Assert.DoesNotContain("Document", json);
            Assert.DoesNotContain("Salary", json);
            Assert.Contains("Employee Test", json);
        }
    }
}
