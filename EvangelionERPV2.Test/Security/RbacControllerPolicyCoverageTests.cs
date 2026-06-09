using EvangelionERPV2.Shared.Enums;
using EvangelionERPV2.Web.Controllers;
using EvangelionERPV2.Web.Security;

namespace EvangelionERPV2.Test.Security
{
    public class RbacControllerPolicyCoverageTests
    {
        [Theory]
        [InlineData(typeof(AuditTrailController), nameof(AuditTrailController.GetAuditTrails), RbacPermissions.Audit.Read)]
        [InlineData(typeof(AuditTrailController), nameof(AuditTrailController.GetAuditTrailsByFilter), RbacPermissions.Audit.Read)]
        [InlineData(typeof(AuditTrailController), nameof(AuditTrailController.GetAuditTrail), RbacPermissions.Audit.Read)]
        [InlineData(typeof(AuditTrailController), nameof(AuditTrailController.CleanupRetention), RbacPermissions.Audit.CleanupRetention)]
        [InlineData(typeof(BillsController), nameof(BillsController.GetByOrder), RbacPermissions.Bills.Read)]
        [InlineData(typeof(BillsController), nameof(BillsController.Generate), RbacPermissions.Bills.Generate)]
        [InlineData(typeof(BillsController), nameof(BillsController.Pdf), RbacPermissions.Bills.DownloadPdf)]
        [InlineData(typeof(CashFlowForecastController), nameof(CashFlowForecastController.GetForecast), RbacPermissions.CashFlowForecast.Read)]
        [InlineData(typeof(CashFlowForecastController), nameof(CashFlowForecastController.GetForecastWithBalanceOverride), RbacPermissions.CashFlowForecast.Read)]
        [InlineData(typeof(CashFlowForecastController), nameof(CashFlowForecastController.RunSimulation), RbacPermissions.CashFlowForecast.Simulate)]
        [InlineData(typeof(CustomerController), nameof(CustomerController.GetCustomers), RbacPermissions.Customers.Read)]
        [InlineData(typeof(CustomerController), nameof(CustomerController.GetCustomersByFilter), RbacPermissions.Customers.Read)]
        [InlineData(typeof(CustomerController), nameof(CustomerController.GetCustomer), RbacPermissions.Customers.Read)]
        [InlineData(typeof(CustomerController), nameof(CustomerController.AddCustomer), RbacPermissions.Customers.Create)]
        [InlineData(typeof(CustomerController), nameof(CustomerController.UpdateCustomer), RbacPermissions.Customers.Update)]
        [InlineData(typeof(CustomerController), nameof(CustomerController.DeleteCustomer), RbacPermissions.Customers.Delete)]
        [InlineData(typeof(EmailController), nameof(EmailController.SendManualEmail), RbacPermissions.Email.Send)]
        [InlineData(typeof(EmailController), nameof(EmailController.SendEmail), RbacPermissions.Email.Send)]
        [InlineData(typeof(EmailController), nameof(EmailController.SendMonthEmail), RbacPermissions.Email.Send)]
        [InlineData(typeof(EmailController), nameof(EmailController.AddEmail), RbacPermissions.Email.Manage)]
        [InlineData(typeof(EmailController), nameof(EmailController.SendStockEmail), RbacPermissions.Email.Send)]
        [InlineData(typeof(EnterpriseController), nameof(EnterpriseController.GetEnterprises), RbacPermissions.Enterprise.Read)]
        [InlineData(typeof(EnterpriseController), nameof(EnterpriseController.GetEnterprisesByFilter), RbacPermissions.Enterprise.Read)]
        [InlineData(typeof(EnterpriseController), nameof(EnterpriseController.GetEnterprise), RbacPermissions.Enterprise.Read)]
        [InlineData(typeof(EnterpriseController), nameof(EnterpriseController.AddEnterprise), RbacPermissions.Enterprise.Create)]
        [InlineData(typeof(EnterpriseController), nameof(EnterpriseController.UpdateEnterprise), RbacPermissions.Enterprise.Update)]
        [InlineData(typeof(EnterpriseController), nameof(EnterpriseController.DeleteEnterprise), RbacPermissions.Enterprise.Delete)]
        [InlineData(typeof(NFeController), nameof(NFeController.GetByOrder), RbacPermissions.NFe.Read)]
        [InlineData(typeof(NFeController), nameof(NFeController.Issue), RbacPermissions.NFe.Issue)]
        [InlineData(typeof(NFeController), nameof(NFeController.Consult), RbacPermissions.NFe.Read)]
        [InlineData(typeof(NFeController), nameof(NFeController.Cancel), RbacPermissions.NFe.Cancel)]
        [InlineData(typeof(OpportunitiesController), nameof(OpportunitiesController.GetOpportunities), RbacPermissions.Opportunities.Read)]
        [InlineData(typeof(OpportunitiesController), nameof(OpportunitiesController.GetOpportunity), RbacPermissions.Opportunities.Read)]
        [InlineData(typeof(OpportunitiesController), nameof(OpportunitiesController.AddFeedback), RbacPermissions.Opportunities.Feedback)]
        [InlineData(typeof(OpportunitiesController), nameof(OpportunitiesController.Recompute), RbacPermissions.Opportunities.Recompute)]
        [InlineData(typeof(OpportunitiesController), nameof(OpportunitiesController.GetSummary), RbacPermissions.Opportunities.Read)]
        [InlineData(typeof(OrderController), nameof(OrderController.GetOrders), RbacPermissions.Orders.Read)]
        [InlineData(typeof(OrderController), nameof(OrderController.GetOrdersByFilter), RbacPermissions.Orders.Read)]
        [InlineData(typeof(OrderController), nameof(OrderController.GetOrder), RbacPermissions.Orders.Read)]
        [InlineData(typeof(OrderController), nameof(OrderController.AddOrder), RbacPermissions.Orders.Create)]
        [InlineData(typeof(OrderController), nameof(OrderController.InsertOrder), RbacPermissions.Orders.Create)]
        [InlineData(typeof(OrderController), nameof(OrderController.UpdateOrder), RbacPermissions.Orders.Update)]
        [InlineData(typeof(OrderController), nameof(OrderController.RefundOrder), RbacPermissions.Orders.Refund)]
        [InlineData(typeof(OrderController), nameof(OrderController.DeleteOrder), RbacPermissions.Orders.Delete)]
        [InlineData(typeof(PayableBillsController), nameof(PayableBillsController.GetPayableBills), RbacPermissions.PayableBills.Read)]
        [InlineData(typeof(PayableBillsController), nameof(PayableBillsController.GetPayableBill), RbacPermissions.PayableBills.Read)]
        [InlineData(typeof(PayableBillsController), nameof(PayableBillsController.AddPayableBill), RbacPermissions.PayableBills.Create)]
        [InlineData(typeof(PayableBillsController), nameof(PayableBillsController.UpdatePayableBill), RbacPermissions.PayableBills.Update)]
        [InlineData(typeof(PayableBillsController), nameof(PayableBillsController.MarkProductsReceived), RbacPermissions.PayableBills.MarkProductsReceived)]
        [InlineData(typeof(PayableBillsController), nameof(PayableBillsController.RefundPayableBill), RbacPermissions.PayableBills.Refund)]
        [InlineData(typeof(PayableBillsController), nameof(PayableBillsController.GetReplenishmentSuggestions), RbacPermissions.PayableBills.Read)]
        [InlineData(typeof(PayableBillsController), nameof(PayableBillsController.DeletePayableBill), RbacPermissions.PayableBills.Delete)]
        [InlineData(typeof(ProductController), nameof(ProductController.GetProducts), RbacPermissions.Products.Read)]
        [InlineData(typeof(ProductController), nameof(ProductController.GetProductsByFilter), RbacPermissions.Products.Read)]
        [InlineData(typeof(ProductController), nameof(ProductController.GetProduct), RbacPermissions.Products.Read)]
        [InlineData(typeof(ProductController), nameof(ProductController.AddProduct), RbacPermissions.Products.Create)]
        [InlineData(typeof(ProductController), nameof(ProductController.UpdateProduct), RbacPermissions.Products.Update)]
        [InlineData(typeof(ProductController), nameof(ProductController.DeleteProduct), RbacPermissions.Products.Delete)]
        [InlineData(typeof(ProductController), nameof(ProductController.UploadPicture), RbacPermissions.Products.UploadPicture)]
        [InlineData(typeof(ReportsController), nameof(ReportsController.GetReports), RbacPermissions.Reports.Read)]
        [InlineData(typeof(ReportsController), nameof(ReportsController.GenerateReport), RbacPermissions.Reports.Generate)]
        [InlineData(typeof(ReportsController), nameof(ReportsController.GetReportById), RbacPermissions.Reports.Read)]
        [InlineData(typeof(UserController), nameof(UserController.GetUsers), RbacPermissions.Users.Read)]
        [InlineData(typeof(UserController), nameof(UserController.AddUser), RbacPermissions.Users.Create)]
        [InlineData(typeof(UserController), nameof(UserController.UpdateUser), RbacPermissions.Users.Update)]
        [InlineData(typeof(UserController), nameof(UserController.UpdateTheme), RbacPermissions.Users.UpdateSelf)]
        [InlineData(typeof(UserController), nameof(UserController.UpdateLanguage), RbacPermissions.Users.UpdateSelf)]
        [InlineData(typeof(UserController), nameof(UserController.UpdateProfilePicture), RbacPermissions.Users.UpdateSelf)]
        [InlineData(typeof(UserController), nameof(UserController.DeleteUser), RbacPermissions.Users.Delete)]
        [InlineData(typeof(UserController), nameof(UserController.GetAccessLevel), RbacPermissions.Users.ReadSelf)]
        public void MigratedEndpoints_DeclareExpectedRbacPolicy(Type controllerType, string actionName, string permission)
        {
            var method = controllerType.GetMethods().Single(x => x.Name == actionName);
            var expectedPolicy = "rbac:" + permission;

            var policy = method
                .GetCustomAttributes(typeof(Microsoft.AspNetCore.Authorization.AuthorizeAttribute), inherit: true)
                .Cast<Microsoft.AspNetCore.Authorization.AuthorizeAttribute>()
                .SingleOrDefault(x => x.Policy == expectedPolicy);

            Assert.NotNull(policy);
        }

        [Fact]
        public void UserGetById_DeclaresSelfOrReadPolicy()
        {
            var method = typeof(UserController).GetMethods().Single(x => x.Name == nameof(UserController.GetUser));
            var policy = method
                .GetCustomAttributes(typeof(Microsoft.AspNetCore.Authorization.AuthorizeAttribute), inherit: true)
                .Cast<Microsoft.AspNetCore.Authorization.AuthorizeAttribute>()
                .SingleOrDefault(x => x.Policy == RbacPolicies.Users.ReadSelfOrRead);

            Assert.NotNull(policy);
        }

        [Fact]
        public void AdminOnlyPermissions_BlockLowerRolesInRoleMap()
        {
            Assert.True(RbacRolePermissionMap.HasPermission((short)EnumAccessLevel.Admin, RbacPermissions.Audit.CleanupRetention));
            Assert.True(RbacRolePermissionMap.HasPermission((short)EnumAccessLevel.Admin, RbacPermissions.Users.Delete));
            Assert.True(RbacRolePermissionMap.HasPermission((short)EnumAccessLevel.Admin, RbacPermissions.Enterprise.Delete));
            Assert.False(RbacRolePermissionMap.HasPermission((short)EnumAccessLevel.Manager, RbacPermissions.Audit.CleanupRetention));
            Assert.False(RbacRolePermissionMap.HasPermission((short)EnumAccessLevel.Supervisor, RbacPermissions.Users.Delete));
            Assert.False(RbacRolePermissionMap.HasPermission((short)EnumAccessLevel.Employee, RbacPermissions.Enterprise.Delete));
        }

        [Fact]
        public void ManagementPermissions_AllowSupervisorAndBlockEmployeeInRoleMap()
        {
            Assert.True(RbacRolePermissionMap.HasPermission((short)EnumAccessLevel.Admin, RbacPermissions.Orders.Update));
            Assert.True(RbacRolePermissionMap.HasPermission((short)EnumAccessLevel.Manager, RbacPermissions.Orders.Update));
            Assert.True(RbacRolePermissionMap.HasPermission((short)EnumAccessLevel.Supervisor, RbacPermissions.Orders.Update));
            Assert.False(RbacRolePermissionMap.HasPermission((short)EnumAccessLevel.Employee, RbacPermissions.Orders.Update));
        }
    }
}
