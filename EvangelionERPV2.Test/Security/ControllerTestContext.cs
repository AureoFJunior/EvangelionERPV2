using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace EvangelionERPV2.Test.Security
{
    /// <summary>
    /// Every controller here reads the tenant from <see cref="ClaimTypes.GroupSid"/> and the caller
    /// from <see cref="ClaimTypes.Sid"/>, and refuses without them. Tests that forget to attach a
    /// principal do not fail on the thing they meant to assert, so the wiring lives here once.
    /// </summary>
    internal static class ControllerTestContext
    {
        public static TController WithTenant<TController>(this TController controller, Guid enterpriseId, Guid? userId = null)
            where TController : ControllerBase
        {
            controller.ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { User = Principal(enterpriseId, userId) }
            };

            return controller;
        }

        /// <summary>A signed-in caller carrying no tenant at all — the shape every action refuses.</summary>
        public static TController WithoutTenant<TController>(this TController controller)
            where TController : ControllerBase
        {
            controller.ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity([], "TestAuth"))
                }
            };

            return controller;
        }

        private static ClaimsPrincipal Principal(Guid enterpriseId, Guid? userId)
        {
            var claims = new List<Claim> { new(ClaimTypes.GroupSid, enterpriseId.ToString()) };

            if (userId.HasValue)
                claims.Add(new Claim(ClaimTypes.Sid, userId.Value.ToString()));

            return new ClaimsPrincipal(new ClaimsIdentity(claims, "TestAuth"));
        }
    }
}
