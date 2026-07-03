using EvangelionERPV2.Shared.Configs;
using EvangelionERPV2.Shared.Entities;
using System.Text.Json;

namespace EvangelionERPV2.Test.Security
{
    public class AuditTrailMapperPrivacyTests
    {
        [Fact]
        public void AuditTrail_UserName_IsMappedToStableNonReversibleIdentifier()
        {
            var mapper = MapperConfig.RegisterMaps().CreateMapper();
            var auditTrail = new AuditTrail
            {
                User = new User
                {
                    UserName = "jane.doe@example.com"
                }
            };

            var dto = mapper.Map<Shared.DTOs.AuditTrailDTO>(auditTrail);

            Assert.NotEqual("jane.doe@example.com", dto.UserName);
            Assert.Matches("^[0-9a-f]{12}$", dto.UserName);
        }

        [Fact]
        public void AuditTrail_ChangesJson_IsNotSerializedToApiResponses()
        {
            var mapper = MapperConfig.RegisterMaps().CreateMapper();
            var auditTrail = new AuditTrail
            {
                ChangesJson = "{\"Email\":{\"OldValue\":\"old@example.com\",\"NewValue\":\"new@example.com\"}}"
            };

            var dto = mapper.Map<Shared.DTOs.AuditTrailDTO>(auditTrail);
            var json = JsonSerializer.Serialize(dto);

            using var document = JsonDocument.Parse(json);
            Assert.False(document.RootElement.TryGetProperty("ChangesJson", out _));
        }
    }
}
