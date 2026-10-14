using System.Security.Claims;
using EvangelionERPV2.StorefrontModule.Application.DTOs;
using EvangelionERPV2.StorefrontModule.Application.Interface;
using EvangelionERPV2.Web.Controllers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Moq;

namespace EvangelionERPV2.Test.Security
{
    /// <summary>SPEC-limen-categories §3.3: the tree is the Limen team's; another enterprise's Admin holds the permission but is refused.</summary>
    public class StorefrontCategoryManageTests
    {
        private static readonly Guid Platform = Guid.NewGuid();

        [Fact]
        public async Task CreateCategory_IsForbiddenToAnotherEnterprise_AndAllowedToThePlatform()
        {
            var categories = new Mock<IStorefrontCategoryService>();
            categories.Setup(s => s.CreateAsync(It.IsAny<SaveStorefrontCategoryRequestDTO>()))
                .ReturnsAsync(new StorefrontCategoryNodeDTO(Guid.NewGuid(), null, "Casa", "casa", "Tag", null, 0, true, 0, 0, []));

            var tenant = await Controller(categories.Object, Guid.NewGuid()).CreateCategory(new SaveStorefrontCategoryRequestDTO());
            Assert.IsType<ForbidResult>(tenant);
            categories.Verify(s => s.CreateAsync(It.IsAny<SaveStorefrontCategoryRequestDTO>()), Times.Never);

            Assert.IsType<OkObjectResult>(await Controller(categories.Object, Platform).CreateCategory(new SaveStorefrontCategoryRequestDTO()));
        }

        private static StorefrontController Controller(IStorefrontCategoryService categories, Guid enterpriseId) =>
            new(Mock.Of<IStorefrontService>(), Mock.Of<IStorefrontCheckoutService>(), categories, null!, null!, null!,
                new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Storefront:PlatformEnterpriseId"] = Platform.ToString()
                }).Build())
            {
                ControllerContext = new ControllerContext
                {
                    HttpContext = new DefaultHttpContext
                    {
                        User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.GroupSid, enterpriseId.ToString())], "test"))
                    }
                }
            };
    }
}
