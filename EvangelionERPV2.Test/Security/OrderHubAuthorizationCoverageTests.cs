using EvangelionERPV2.Shared.Hubs;
using EvangelionERPV2.Web.Security;
using Microsoft.AspNetCore.Authorization;

namespace EvangelionERPV2.Test.Security
{
    public class OrderHubAuthorizationCoverageTests
    {
        [Fact]
        public void OrderHub_DeclaresOrdersReadPolicy()
        {
            var policy = typeof(OrderHub)
                .GetCustomAttributes(typeof(AuthorizeAttribute), inherit: true)
                .Cast<AuthorizeAttribute>()
                .SingleOrDefault(x => x.Policy == "rbac:" + RbacPermissions.Orders.Read);

            Assert.NotNull(policy);
        }
    }
}
