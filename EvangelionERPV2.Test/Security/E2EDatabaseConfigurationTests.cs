using System.Reflection;
using EvangelionERPV2.Web.Controllers;

namespace EvangelionERPV2.Test.Security;

public sealed class E2EDatabaseConfigurationTests
{
    private static readonly MethodInfo Validate = typeof(OrderController).Assembly
        .GetType("Program", throwOnError: true)!
        .GetMethods(BindingFlags.NonPublic | BindingFlags.Static)
        .Single(method => method.Name.Contains("ValidateE2EConnectionString", StringComparison.Ordinal));

    [Fact]
    public void AcceptsOnlyTheDedicatedE2ESqlHostAndDatabase()
    {
        Validate.Invoke(null, ["plain:Server=e2e-sqlserver;Initial Catalog=ERP_E2E;User ID=test;Password=dummy"]);

        AssertInvalid("Server=production-sql;Initial Catalog=ERP_E2E;User ID=test;Password=dummy");
        AssertInvalid("Server=e2e-sqlserver;Initial Catalog=ERP;User ID=test;Password=dummy");
        AssertInvalid(null);
    }

    private static void AssertInvalid(string? connectionString)
    {
        var exception = Assert.Throws<TargetInvocationException>(() => Validate.Invoke(null, [connectionString]));
        Assert.IsType<InvalidOperationException>(exception.InnerException);
    }
}
