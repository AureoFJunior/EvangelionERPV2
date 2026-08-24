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
        [InlineData(typeof(BankReconciliationController), nameof(BankReconciliationController.ImportStatement), RbacPermissions.BankReconciliation.Import)]
        [InlineData(typeof(BankReconciliationController), nameof(BankReconciliationController.GetUnclearedLines), RbacPermissions.BankReconciliation.Read)]
        [InlineData(typeof(BankReconciliationController), nameof(BankReconciliationController.GetVariance), RbacPermissions.BankReconciliation.Read)]
        [InlineData(typeof(BankReconciliationController), nameof(BankReconciliationController.MatchLine), RbacPermissions.BankReconciliation.Resolve)]
        [InlineData(typeof(BankReconciliationController), nameof(BankReconciliationController.IgnoreLine), RbacPermissions.BankReconciliation.Resolve)]
        [InlineData(typeof(DashboardController), nameof(DashboardController.Today), RbacPermissions.Dashboard.Read)]
        [InlineData(typeof(BillsController), nameof(BillsController.GetByOrder), RbacPermissions.Bills.Read)]
        [InlineData(typeof(BillsController), nameof(BillsController.Generate), RbacPermissions.Bills.Generate)]
        [InlineData(typeof(BillsController), nameof(BillsController.Pdf), RbacPermissions.Bills.DownloadPdf)]
        [InlineData(typeof(BillsController), nameof(BillsController.History), RbacPermissions.Bills.Read)]
        [InlineData(typeof(BillsController), nameof(BillsController.Settle), RbacPermissions.Bills.Settle)]
        [InlineData(typeof(BillsController), nameof(BillsController.Reissue), RbacPermissions.Bills.Reissue)]
        [InlineData(typeof(BillsController), nameof(BillsController.Aging), RbacPermissions.Bills.Read)]
        [InlineData(typeof(BillsController), nameof(BillsController.Delinquent), RbacPermissions.Bills.Read)]
        [InlineData(typeof(BillsController), nameof(BillsController.Statement), RbacPermissions.Bills.Read)]
        [InlineData(typeof(BillsController), nameof(BillsController.AccountingPackage), RbacPermissions.Accounting.Export)]
        [InlineData(typeof(CashFlowForecastController), nameof(CashFlowForecastController.GetForecast), RbacPermissions.CashFlowForecast.Read)]
        [InlineData(typeof(CashFlowForecastController), nameof(CashFlowForecastController.GetForecastWithBalanceOverride), RbacPermissions.CashFlowForecast.Read)]
        [InlineData(typeof(CashFlowForecastController), nameof(CashFlowForecastController.RunSimulation), RbacPermissions.CashFlowForecast.Simulate)]
        [InlineData(typeof(CustomerController), nameof(CustomerController.GetCustomers), RbacPermissions.Customers.Read)]
        [InlineData(typeof(CustomerController), nameof(CustomerController.GetCustomersByFilter), RbacPermissions.Customers.Read)]
        [InlineData(typeof(CustomerController), nameof(CustomerController.GetCustomer), RbacPermissions.Customers.Read)]
        [InlineData(typeof(CustomerController), nameof(CustomerController.AddCustomer), RbacPermissions.Customers.Create)]
        [InlineData(typeof(CustomerController), nameof(CustomerController.UpdateCustomer), RbacPermissions.Customers.Update)]
        [InlineData(typeof(CustomerController), nameof(CustomerController.DeleteCustomer), RbacPermissions.Customers.Delete)]
        [InlineData(typeof(SupplierController), nameof(SupplierController.GetSuppliers), RbacPermissions.Suppliers.Read)]
        [InlineData(typeof(SupplierController), nameof(SupplierController.GetSupplier), RbacPermissions.Suppliers.Read)]
        [InlineData(typeof(SupplierController), nameof(SupplierController.AddSupplier), RbacPermissions.Suppliers.Create)]
        [InlineData(typeof(SupplierController), nameof(SupplierController.UpdateSupplier), RbacPermissions.Suppliers.Update)]
        [InlineData(typeof(SupplierController), nameof(SupplierController.DeleteSupplier), RbacPermissions.Suppliers.Delete)]
        [InlineData(typeof(EmailController), nameof(EmailController.SendManualEmail), RbacPermissions.Email.Send)]
        [InlineData(typeof(EmailController), nameof(EmailController.SendEmail), RbacPermissions.Email.Send)]
        [InlineData(typeof(EmailController), nameof(EmailController.SendMonthEmail), RbacPermissions.Email.Send)]
        [InlineData(typeof(EmailController), nameof(EmailController.AddEmail), RbacPermissions.Email.Manage)]
        [InlineData(typeof(EmailController), nameof(EmailController.SendStockEmail), RbacPermissions.Email.Send)]
        [InlineData(typeof(EmployeeController), nameof(EmployeeController.GetEmployees), RbacPermissions.Employees.Read)]
        [InlineData(typeof(EmployeeController), nameof(EmployeeController.GetEmployeesByFilter), RbacPermissions.Employees.Read)]
        [InlineData(typeof(EmployeeController), nameof(EmployeeController.GetEmployee), RbacPermissions.Employees.Read)]
        [InlineData(typeof(EmployeeController), nameof(EmployeeController.AddEmployee), RbacPermissions.Employees.Create)]
        [InlineData(typeof(EmployeeController), nameof(EmployeeController.UpdateEmployee), RbacPermissions.Employees.Update)]
        [InlineData(typeof(EmployeeController), nameof(EmployeeController.DeleteEmployee), RbacPermissions.Employees.Delete)]
        [InlineData(typeof(EmployeeAbsenceController), nameof(EmployeeAbsenceController.GetEmployeeAbsences), RbacPermissions.EmployeeAbsences.Read)]
        [InlineData(typeof(EmployeeAbsenceController), nameof(EmployeeAbsenceController.GetEmployeeAbsence), RbacPermissions.EmployeeAbsences.Read)]
        [InlineData(typeof(EmployeeAbsenceController), nameof(EmployeeAbsenceController.AddEmployeeAbsence), RbacPermissions.EmployeeAbsences.Create)]
        [InlineData(typeof(EmployeeAbsenceController), nameof(EmployeeAbsenceController.UpdateEmployeeAbsence), RbacPermissions.EmployeeAbsences.Update)]
        [InlineData(typeof(EmployeeAbsenceController), nameof(EmployeeAbsenceController.DeleteEmployeeAbsence), RbacPermissions.EmployeeAbsences.Delete)]
        [InlineData(typeof(EmployeeVacationController), nameof(EmployeeVacationController.GetVacationAccruals), RbacPermissions.EmployeeVacations.Read)]
        [InlineData(typeof(EmployeeVacationController), nameof(EmployeeVacationController.RefreshVacationAccruals), RbacPermissions.EmployeeVacations.Schedule)]
        [InlineData(typeof(EmployeeVacationController), nameof(EmployeeVacationController.ScheduleVacation), RbacPermissions.EmployeeVacations.Schedule)]
        [InlineData(typeof(EmployeeVacationController), nameof(EmployeeVacationController.SellVacationDays), RbacPermissions.EmployeeVacations.Schedule)]
        [InlineData(typeof(EnterpriseController), nameof(EnterpriseController.GetEnterprises), RbacPermissions.Enterprise.Read)]
        [InlineData(typeof(EnterpriseController), nameof(EnterpriseController.GetEnterprisesByFilter), RbacPermissions.Enterprise.Read)]
        [InlineData(typeof(EnterpriseController), nameof(EnterpriseController.GetEnterprise), RbacPermissions.Enterprise.Read)]
        [InlineData(typeof(EnterpriseController), nameof(EnterpriseController.GetMyEnterprise), RbacPermissions.Enterprise.ReadSelf)]
        [InlineData(typeof(EnterpriseController), nameof(EnterpriseController.AddEnterprise), RbacPermissions.Enterprise.Create)]
        [InlineData(typeof(EnterpriseController), nameof(EnterpriseController.UpdateEnterprise), RbacPermissions.Enterprise.Update)]
        [InlineData(typeof(EnterpriseController), nameof(EnterpriseController.DeleteEnterprise), RbacPermissions.Enterprise.Delete)]
        [InlineData(typeof(NFeController), nameof(NFeController.GetByOrder), RbacPermissions.NFe.Read)]
        [InlineData(typeof(NFeController), nameof(NFeController.Issue), RbacPermissions.NFe.Issue)]
        [InlineData(typeof(NFeController), nameof(NFeController.Consult), RbacPermissions.NFe.Read)]
        [InlineData(typeof(NFeController), nameof(NFeController.Cancel), RbacPermissions.NFe.Cancel)]
        [InlineData(typeof(NFeFiscalSettingsController), nameof(NFeFiscalSettingsController.GetFiscalSettings), RbacPermissions.NFe.SettingsRead)]
        [InlineData(typeof(NFeFiscalSettingsController), nameof(NFeFiscalSettingsController.UpdateFiscalSettings), RbacPermissions.NFe.SettingsUpdate)]
        [InlineData(typeof(NFeFiscalSettingsController), nameof(NFeFiscalSettingsController.UploadCertificate), RbacPermissions.NFe.CertificateManage)]
        [InlineData(typeof(NFeFiscalSettingsController), nameof(NFeFiscalSettingsController.GetCertificateStatus), RbacPermissions.NFe.CertificateRead)]
        [InlineData(typeof(NFeFiscalSettingsController), nameof(NFeFiscalSettingsController.DeleteCertificate), RbacPermissions.NFe.CertificateManage)]
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
        [InlineData(typeof(PayrollController), nameof(PayrollController.GetPayrollSettings), RbacPermissions.Payroll.Read)]
        [InlineData(typeof(PayrollController), nameof(PayrollController.UpdatePayrollSettings), RbacPermissions.Payroll.UpdateSettings)]
        [InlineData(typeof(PayrollController), nameof(PayrollController.GeneratePayroll), RbacPermissions.Payroll.Generate)]
        [InlineData(typeof(PayrollController), nameof(PayrollController.GetPayroll), RbacPermissions.Payroll.Read)]
        [InlineData(typeof(PayrollController), nameof(PayrollController.GenerateVacationPay), RbacPermissions.Payroll.Generate)]
        [InlineData(typeof(PayrollController), nameof(PayrollController.GetVacationPay), RbacPermissions.Payroll.Read)]
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

        [Theory]
        [InlineData(typeof(EmailController), nameof(EmailController.SendMonthlyBillingBroadcast))]
        [InlineData(typeof(EmailController), nameof(EmailController.SendWeeklyStockBroadcast))]
        [InlineData(typeof(OrderController), nameof(OrderController.InsertQueuedOrder))]
        public void BroadcastEndpoints_DeclareSelfApiMachinePolicy(Type controllerType, string actionName)
        {
            var method = controllerType.GetMethods().Single(x => x.Name == actionName);
            var policy = method
                .GetCustomAttributes(typeof(Microsoft.AspNetCore.Authorization.AuthorizeAttribute), inherit: true)
                .Cast<Microsoft.AspNetCore.Authorization.AuthorizeAttribute>()
                .SingleOrDefault(x => x.Policy == RbacPolicies.Machine.SelfApiBroadcast);

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
        public void FiscalSettingsPermissions_AreAdminOnlyInRoleMap()
        {
            // Admin-only by omission from every role array. The fiscal profile exposes the
            // emitente CNPJ and IE, and a wrong CFOP, CST or CRT silently mis-taxes every future
            // document — only fixable by cancelling inside the legal window.
            foreach (var permission in new[]
                     {
                         RbacPermissions.NFe.SettingsRead,
                         RbacPermissions.NFe.SettingsUpdate,
                         RbacPermissions.NFe.CertificateManage
                     })
            {
                Assert.True(RbacRolePermissionMap.HasPermission((short)EnumAccessLevel.Admin, permission));
                Assert.False(RbacRolePermissionMap.HasPermission((short)EnumAccessLevel.Manager, permission));
                Assert.False(RbacRolePermissionMap.HasPermission((short)EnumAccessLevel.Supervisor, permission));
                Assert.False(RbacRolePermissionMap.HasPermission((short)EnumAccessLevel.Employee, permission));
            }
        }

        [Fact]
        public void CertificateRead_StartsAtManagerInRoleMap()
        {
            // Metadata only (subject, expiry, days remaining) — no key material. Whoever operates
            // issuance needs the expiry warning without waiting on an admin, but replacing the
            // certificate stays admin-only.
            Assert.True(RbacRolePermissionMap.HasPermission((short)EnumAccessLevel.Admin, RbacPermissions.NFe.CertificateRead));
            Assert.True(RbacRolePermissionMap.HasPermission((short)EnumAccessLevel.Manager, RbacPermissions.NFe.CertificateRead));
            Assert.False(RbacRolePermissionMap.HasPermission((short)EnumAccessLevel.Supervisor, RbacPermissions.NFe.CertificateRead));
            Assert.False(RbacRolePermissionMap.HasPermission((short)EnumAccessLevel.Employee, RbacPermissions.NFe.CertificateRead));
        }

        [Fact]
        public void EnterpriseReadSelf_StartsAtManagerInRoleMap()
        {
            // Enterprise self-read exposes company-level financials, so it is deliberately
            // NOT part of the base (Employee/Supervisor) permission set.
            Assert.True(RbacRolePermissionMap.HasPermission((short)EnumAccessLevel.Admin, RbacPermissions.Enterprise.ReadSelf));
            Assert.True(RbacRolePermissionMap.HasPermission((short)EnumAccessLevel.Manager, RbacPermissions.Enterprise.ReadSelf));
            Assert.False(RbacRolePermissionMap.HasPermission((short)EnumAccessLevel.Supervisor, RbacPermissions.Enterprise.ReadSelf));
            Assert.False(RbacRolePermissionMap.HasPermission((short)EnumAccessLevel.Employee, RbacPermissions.Enterprise.ReadSelf));
        }

        [Fact]
        public void PayrollPermissions_AreAdminOnlyInRoleMap()
        {
            Assert.True(RbacRolePermissionMap.HasPermission((short)EnumAccessLevel.Admin, RbacPermissions.Payroll.Read));
            Assert.True(RbacRolePermissionMap.HasPermission((short)EnumAccessLevel.Admin, RbacPermissions.Payroll.Generate));
            Assert.True(RbacRolePermissionMap.HasPermission((short)EnumAccessLevel.Admin, RbacPermissions.Payroll.UpdateSettings));
            Assert.False(RbacRolePermissionMap.HasPermission((short)EnumAccessLevel.Manager, RbacPermissions.Payroll.Read));
            Assert.False(RbacRolePermissionMap.HasPermission((short)EnumAccessLevel.Manager, RbacPermissions.Payroll.Generate));
            Assert.False(RbacRolePermissionMap.HasPermission((short)EnumAccessLevel.Manager, RbacPermissions.Payroll.UpdateSettings));
            Assert.False(RbacRolePermissionMap.HasPermission((short)EnumAccessLevel.Supervisor, RbacPermissions.Payroll.Read));
            Assert.False(RbacRolePermissionMap.HasPermission((short)EnumAccessLevel.Employee, RbacPermissions.Payroll.Read));
        }

        /// <summary>
        /// Absences sit a tier above the employee record they hang off: an atestado carries a CID
        /// (LGPD art. 11 health data) and IsJustified decides whether a day is paid, which makes
        /// write access here write access to payroll. Manager holds every Employees permission and
        /// must still hold none of these.
        /// </summary>
        [Fact]
        public void EmployeeAbsencePermissions_AreAdminOnlyInRoleMap()
        {
            Assert.True(RbacRolePermissionMap.HasPermission((short)EnumAccessLevel.Manager, RbacPermissions.Employees.Read));

            foreach (var permission in new[]
                     {
                         RbacPermissions.EmployeeAbsences.Read,
                         RbacPermissions.EmployeeAbsences.Create,
                         RbacPermissions.EmployeeAbsences.Update,
                         RbacPermissions.EmployeeAbsences.Delete,

                         // Same tier: scheduling férias writes an absence row, and a lapsed
                         // accrual is a debt owed in dobro.
                         RbacPermissions.EmployeeVacations.Read,
                         RbacPermissions.EmployeeVacations.Schedule
                     })
            {
                Assert.True(RbacRolePermissionMap.HasPermission((short)EnumAccessLevel.Admin, permission));
                Assert.False(RbacRolePermissionMap.HasPermission((short)EnumAccessLevel.Manager, permission));
                Assert.False(RbacRolePermissionMap.HasPermission((short)EnumAccessLevel.Supervisor, permission));
                Assert.False(RbacRolePermissionMap.HasPermission((short)EnumAccessLevel.Employee, permission));
            }
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
