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

            Audit.Read,
            Audit.CleanupRetention,

            Bills.Read,
            Bills.Generate,
            Bills.DownloadPdf,

            CashFlowForecast.Read,
            CashFlowForecast.Simulate,

            Customers.Read,
            Customers.Create,
            Customers.Update,
            Customers.Delete,

            Email.Manage,
            Email.Send,

            Enterprise.Read,
            Enterprise.Create,
            Enterprise.Update,
            Enterprise.Delete,
            Enterprise.Manage,

            NFe.Read,
            NFe.Issue,
            NFe.Cancel,

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

            Products.Read,
            Products.Create,
            Products.Update,
            Products.Delete,
            Products.UploadPicture,
            Products.DeletePicture,

            Reports.Read,
            Reports.Generate,

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
        }

        private static string BuildPolicyName(string permission)
        {
            return $"rbac:{permission}";
        }

        public static class Metrics
        {
            public const string Read = "rbac:metrics.read";
        }

        public static class Health
        {
            public const string Read = "rbac:health.read";
        }

        public static class Audit
        {
            public const string Read = "rbac:audit.read";
            public const string CleanupRetention = "rbac:audit.retention.cleanup";
        }

        public static class Bills
        {
            public const string Read = "rbac:bills.read";
            public const string Generate = "rbac:bills.generate";
            public const string DownloadPdf = "rbac:bills.pdf.download";
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
        }

        public static class Email
        {
            public const string Manage = "rbac:email.manage";
            public const string Send = "rbac:email.send";
        }

        public static class Enterprise
        {
            public const string Read = "rbac:enterprise.read";
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

        public static class Products
        {
            public const string Read = "rbac:products.read";
            public const string Create = "rbac:products.create";
            public const string Update = "rbac:products.update";
            public const string Delete = "rbac:products.delete";
            public const string UploadPicture = "rbac:products.picture.upload";
            public const string DeletePicture = "rbac:products.picture.delete";
        }

        public static class Reports
        {
            public const string Read = "rbac:reports.read";
            public const string Generate = "rbac:reports.generate";
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
