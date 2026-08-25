using EvangelionERPV2.Shared.Security;
using Microsoft.AspNetCore.Authorization;

namespace EvangelionERPV2.Web.Security
{
    public static class RbacPolicies
    {
        public const string LegacyInternalMetricsAccess = "InternalMetricsAccess";

        public static IReadOnlyCollection<string> All { get; } =
        [
            Metrics.Read,
            Health.Read,

            Accounting.Export,

            Audit.Read,
            Audit.CleanupRetention,

            BankReconciliation.Read,
            BankReconciliation.Import,
            BankReconciliation.Resolve,

            Dashboard.Read,

            Bills.Read,
            Bills.Generate,
            Bills.DownloadPdf,
            Bills.Settle,
            Bills.Reissue,

            CashFlowForecast.Read,
            CashFlowForecast.Simulate,

            Customers.Read,
            Customers.Create,
            Customers.Update,
            Customers.Delete,
            Customers.CreditOverride,

            EmployeeAbsences.Read,
            EmployeeAbsences.Create,
            EmployeeAbsences.Update,
            EmployeeAbsences.Delete,

            EmployeeVacations.Read,
            EmployeeVacations.Schedule,

            Email.Manage,
            Email.Send,

            Enterprise.Read,
            Enterprise.ReadSelf,
            Enterprise.Create,
            Enterprise.Update,
            Enterprise.Delete,
            Enterprise.Manage,

            NFe.Read,
            NFe.Issue,
            NFe.Cancel,
            NFe.SettingsRead,
            NFe.SettingsUpdate,
            NFe.CertificateManage,
            NFe.CertificateRead,

            Opportunities.Read,
            Opportunities.Feedback,
            Opportunities.Recompute,

            Orders.Read,
            Orders.Create,
            Orders.Update,
            Orders.Refund,
            Orders.Delete,

            PayableBills.Read,
            PayableBills.Create,
            PayableBills.Update,
            PayableBills.MarkProductsReceived,
            PayableBills.Refund,
            PayableBills.Delete,

            Payroll.Read,
            Payroll.Generate,
            Payroll.UpdateSettings,

            Quotes.Read,
            Quotes.Create,
            Quotes.Update,
            Quotes.Convert,
            Quotes.Delete,

            SalesPerformance.Read,
            SalesPerformance.ManageGoals,

            Products.Read,
            Products.Create,
            Products.Update,
            Products.Delete,
            Products.UploadPicture,
            Products.DeletePicture,
            Products.ReadCost,

            Reports.Read,
            Reports.Generate,

            Suppliers.Read,
            Suppliers.Create,
            Suppliers.Update,
            Suppliers.Delete,

            Users.Read,
            Users.ReadSelfOrRead,
            Users.ReadSelf,
            Users.Create,
            Users.Update,
            Users.UpdateSelf,
            Users.Delete,
            Users.Manage
        ];

        public static IReadOnlyDictionary<string, string> PermissionToPolicyName { get; } =
            RbacPermissions.All.ToDictionary(permission => permission, BuildPolicyName, StringComparer.OrdinalIgnoreCase);

        public static string ForPermission(string permission)
        {
            if (PermissionToPolicyName.TryGetValue(permission, out var policyName))
                return policyName;

            throw new ArgumentOutOfRangeException(nameof(permission), permission, "Unknown RBAC permission.");
        }

        public static void AddRbacPolicies(this AuthorizationOptions options)
        {
            foreach (var permission in RbacPermissions.All)
            {
                options.AddPolicy(ForPermission(permission), policy =>
                {
                    policy.RequireAuthenticatedUser();
                    policy.AddRequirements(new PermissionRequirement(permission));
                });
            }

            options.AddPolicy(Users.ReadSelfOrRead, policy =>
            {
                policy.RequireAuthenticatedUser();
                policy.AddRequirements(new SelfOrPermissionRequirement(
                    RbacPermissions.Users.ReadSelf,
                    RbacPermissions.Users.Read,
                    "id"));
            });

            options.AddPolicy(LegacyInternalMetricsAccess, policy =>
            {
                policy.RequireAuthenticatedUser();
                policy.AddRequirements(new PermissionRequirement(RbacPermissions.Metrics.Read));
            });

            options.AddPolicy(Machine.SelfApiBroadcast, policy =>
            {
                policy.RequireAuthenticatedUser();
                policy.RequireClaim(MachineClaims.ClientType, MachineClaims.SelfApiValue);
            });
        }

        private static string BuildPolicyName(string permission)
        {
            return $"rbac:{permission}";
        }

        public static class Machine
        {
            public const string SelfApiBroadcast = "machine:self-api-broadcast";
        }

        public static class Metrics
        {
            public const string Read = "rbac:metrics.read";
        }

        public static class Health
        {
            public const string Read = "rbac:health.read";
        }

        public static class Accounting
        {
            public const string Export = "rbac:accounting.export";
        }

        public static class Audit
        {
            public const string Read = "rbac:audit.read";
            public const string CleanupRetention = "rbac:audit.retention.cleanup";
        }

        public static class BankReconciliation
        {
            public const string Read = "rbac:bank-reconciliation.read";
            public const string Import = "rbac:bank-reconciliation.import";
            public const string Resolve = "rbac:bank-reconciliation.resolve";
        }

        public static class Dashboard
        {
            public const string Read = "rbac:dashboard.read";
        }

        public static class Bills
        {
            public const string Read = "rbac:bills.read";
            public const string Generate = "rbac:bills.generate";
            public const string DownloadPdf = "rbac:bills.pdf.download";
            public const string Settle = "rbac:bills.settle";
            public const string Reissue = "rbac:bills.reissue";
        }

        public static class CashFlowForecast
        {
            public const string Read = "rbac:cashflow.forecast.read";
            public const string Simulate = "rbac:cashflow.forecast.simulate";
        }

        public static class Customers
        {
            public const string Read = "rbac:customers.read";
            public const string Create = "rbac:customers.create";
            public const string Update = "rbac:customers.update";
            public const string Delete = "rbac:customers.delete";
            public const string CreditOverride = "rbac:customers.credit.override";
        }

        public static class EmployeeAbsences
        {
            public const string Read = "rbac:employee-absences.read";
            public const string Create = "rbac:employee-absences.create";
            public const string Update = "rbac:employee-absences.update";
            public const string Delete = "rbac:employee-absences.delete";
        }

        public static class EmployeeVacations
        {
            public const string Read = "rbac:employee-vacations.read";
            public const string Schedule = "rbac:employee-vacations.schedule";
        }

        public static class Email
        {
            public const string Manage = "rbac:email.manage";
            public const string Send = "rbac:email.send";
        }

        public static class Enterprise
        {
            public const string Read = "rbac:enterprise.read";
            public const string ReadSelf = "rbac:enterprise.self.read";
            public const string Create = "rbac:enterprise.create";
            public const string Update = "rbac:enterprise.update";
            public const string Delete = "rbac:enterprise.delete";
            public const string Manage = "rbac:enterprise.manage";
        }

        public static class NFe
        {
            public const string Read = "rbac:nfe.read";
            public const string Issue = "rbac:nfe.issue";
            public const string Cancel = "rbac:nfe.cancel";
            public const string SettingsRead = "rbac:nfe.settings.read";
            public const string SettingsUpdate = "rbac:nfe.settings.update";
            public const string CertificateManage = "rbac:nfe.certificate.manage";
            public const string CertificateRead = "rbac:nfe.certificate.read";
        }

        public static class Opportunities
        {
            public const string Read = "rbac:opportunities.read";
            public const string Feedback = "rbac:opportunities.feedback";
            public const string Recompute = "rbac:opportunities.recompute";
        }

        public static class Orders
        {
            public const string Read = "rbac:orders.read";
            public const string Create = "rbac:orders.create";
            public const string Update = "rbac:orders.update";
            public const string Refund = "rbac:orders.refund";
            public const string Delete = "rbac:orders.delete";
        }

        public static class PayableBills
        {
            public const string Read = "rbac:payable-bills.read";
            public const string Create = "rbac:payable-bills.create";
            public const string Update = "rbac:payable-bills.update";
            public const string MarkProductsReceived = "rbac:payable-bills.products.receive";
            public const string Refund = "rbac:payable-bills.refund";
            public const string Delete = "rbac:payable-bills.delete";
        }

        public static class Payroll
        {
            public const string Read = "rbac:payroll.read";
            public const string Generate = "rbac:payroll.generate";
            public const string UpdateSettings = "rbac:payroll.settings.update";
        }

        public static class SalesPerformance
        {
            public const string Read = "rbac:sales.performance.read";
            public const string ManageGoals = "rbac:sales.goals.manage";
        }

        public static class Products
        {
            public const string Read = "rbac:products.read";
            public const string Create = "rbac:products.create";
            public const string Update = "rbac:products.update";
            public const string Delete = "rbac:products.delete";
            public const string UploadPicture = "rbac:products.picture.upload";
            public const string DeletePicture = "rbac:products.picture.delete";
            public const string ReadCost = "rbac:products.cost.read";
        }

        public static class Quotes
        {
            public const string Read = "rbac:quotes.read";
            public const string Create = "rbac:quotes.create";
            public const string Update = "rbac:quotes.update";
            public const string Convert = "rbac:quotes.convert";
            public const string Delete = "rbac:quotes.delete";
        }

        public static class Reports
        {
            public const string Read = "rbac:reports.read";
            public const string Generate = "rbac:reports.generate";
        }

        public static class Suppliers
        {
            public const string Read = "rbac:suppliers.read";
            public const string Create = "rbac:suppliers.create";
            public const string Update = "rbac:suppliers.update";
            public const string Delete = "rbac:suppliers.delete";
        }

        public static class Users
        {
            public const string Read = "rbac:users.read";
            public const string ReadSelfOrRead = "rbac:users.self-or-read";
            public const string ReadSelf = "rbac:users.self.read";
            public const string Create = "rbac:users.create";
            public const string Update = "rbac:users.update";
            public const string UpdateSelf = "rbac:users.self.update";
            public const string Delete = "rbac:users.delete";
            public const string Manage = "rbac:users.manage";
        }
    }
}
