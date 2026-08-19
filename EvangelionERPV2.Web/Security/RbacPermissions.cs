namespace EvangelionERPV2.Web.Security
{
    public static class RbacPermissions
    {
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

            Employees.Read,
            Employees.Create,
            Employees.Update,
            Employees.Delete,
            Employees.UploadPicture,

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

            Products.Read,
            Products.Create,
            Products.Update,
            Products.Delete,
            Products.UploadPicture,
            Products.DeletePicture,

            Reports.Read,
            Reports.Generate,

            Users.Read,
            Users.ReadSelf,
            Users.Create,
            Users.Update,
            Users.UpdateSelf,
            Users.Delete,
            Users.Manage
        ];

        public static class Metrics
        {
            public const string Read = "metrics.read";
        }

        public static class Health
        {
            public const string Read = "health.read";
        }

        public static class Audit
        {
            public const string Read = "audit.read";
            public const string CleanupRetention = "audit.retention.cleanup";
        }

        public static class Bills
        {
            public const string Read = "bills.read";
            public const string Generate = "bills.generate";
            public const string DownloadPdf = "bills.pdf.download";
        }

        public static class CashFlowForecast
        {
            public const string Read = "cashflow.forecast.read";
            public const string Simulate = "cashflow.forecast.simulate";
        }

        public static class Customers
        {
            public const string Read = "customers.read";
            public const string Create = "customers.create";
            public const string Update = "customers.update";
            public const string Delete = "customers.delete";
        }

        public static class Employees
        {
            public const string Read = "employees.read";
            public const string Create = "employees.create";
            public const string Update = "employees.update";
            public const string Delete = "employees.delete";
            public const string UploadPicture = "employees.picture.upload";
        }

        public static class Email
        {
            public const string Manage = "email.manage";
            public const string Send = "email.send";
        }

        public static class Enterprise
        {
            public const string Read = "enterprise.read";
            // Read the user's own enterprise (tenant from token claim) — all roles.
            public const string ReadSelf = "enterprise.self.read";
            public const string Create = "enterprise.create";
            public const string Update = "enterprise.update";
            public const string Delete = "enterprise.delete";
            public const string Manage = "enterprise.manage";
        }

        public static class NFe
        {
            public const string Read = "nfe.read";
            public const string Issue = "nfe.issue";
            public const string Cancel = "nfe.cancel";

            // Admin-only pair (present in RbacPermissions.All, deliberately absent from every
            // role array): the fiscal profile exposes the emitente CNPJ and IE, and a wrong
            // CFOP, CST or CRT silently mis-taxes every future document — a mistake only
            // fixable by cancelling inside the legal window. Same risk class as payroll.*.
            public const string SettingsRead = "nfe.settings.read";
            public const string SettingsUpdate = "nfe.settings.update";

            // Also admin-only by omission: the holder can replace the company's digital signing
            // identity, and a wrong certificate stops issuance for the whole tenant. Rotated
            // roughly once a year, so there is no operational reason to grant it wider.
            public const string CertificateManage = "nfe.certificate.manage";

            // Metadata only (subject, expiry, days remaining) — no key material, no password.
            // Granted down to Manager so whoever operates can see "12 days left" without
            // waiting on an admin.
            public const string CertificateRead = "nfe.certificate.read";
        }

        public static class Opportunities
        {
            public const string Read = "opportunities.read";
            public const string Feedback = "opportunities.feedback";
            public const string Recompute = "opportunities.recompute";
        }

        public static class Orders
        {
            public const string Read = "orders.read";
            public const string Create = "orders.create";
            public const string Update = "orders.update";
            public const string Refund = "orders.refund";
            public const string Delete = "orders.delete";
        }

        public static class PayableBills
        {
            public const string Read = "payable-bills.read";
            public const string Create = "payable-bills.create";
            public const string Update = "payable-bills.update";
            public const string MarkProductsReceived = "payable-bills.products.receive";
            public const string Refund = "payable-bills.refund";
            public const string Delete = "payable-bills.delete";
        }

        public static class Payroll
        {
            // Admin-only group (granted via RbacPermissions.All, never mapped to other
            // roles): payroll data exposes individual salaries (LGPD) and settings
            // changes alter every future payroll payable amount.
            public const string Read = "payroll.read";
            public const string Generate = "payroll.generate";
            public const string UpdateSettings = "payroll.settings.update";
        }

        public static class Products
        {
            public const string Read = "products.read";
            public const string Create = "products.create";
            public const string Update = "products.update";
            public const string Delete = "products.delete";
            public const string UploadPicture = "products.picture.upload";
            public const string DeletePicture = "products.picture.delete";
        }

        public static class Reports
        {
            public const string Read = "reports.read";
            public const string Generate = "reports.generate";
        }

        public static class Users
        {
            public const string Read = "users.read";
            public const string ReadSelf = "users.self.read";
            public const string Create = "users.create";
            public const string Update = "users.update";
            public const string UpdateSelf = "users.self.update";
            public const string Delete = "users.delete";
            public const string Manage = "users.manage";
        }
    }
}
