using EvangelionERPV2.Shared.Context;
using EvangelionERPV2.Shared.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace EvangelionERPV2.Test.Security
{
    public class UserEmailUniquenessModelTests
    {
        [Fact]
        public void AppDbContext_ActiveUserEmailIndex_IsGlobalAndUnique()
        {
            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseSqlServer("Server=(localdb)\\mssqllocaldb;Database=EvangelionERPV2_ModelTests;Trusted_Connection=True;")
                .Options;

            using var context = new AppDbContext(options);
            var userEntity = context.Model.FindEntityType(typeof(User));

            Assert.NotNull(userEntity);

            var emailIndex = userEntity!.GetIndexes()
                .Single(index => index.GetDatabaseName() == "IX_User_NormalizedActiveEmail_Active");

            Assert.True(emailIndex.IsUnique);
            Assert.Single(emailIndex.Properties);
            Assert.Equal("NormalizedActiveEmail", emailIndex.Properties[0].Name);
            Assert.Equal("[IsActive] = 1 AND [NormalizedActiveEmail] IS NOT NULL", emailIndex.GetFilter());

            Assert.DoesNotContain(
                userEntity.GetIndexes(),
                index => index.GetDatabaseName() == "IX_User_EnterpriseId_NormalizedActiveEmail_Active");
        }
    }
}
