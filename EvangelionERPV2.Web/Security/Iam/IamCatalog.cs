namespace EvangelionERPV2.Web.Security.Iam
{
    public enum PermissionTier { Standard, Sensitive, Privileged }

    public sealed record CatalogPermission(string Permission, string Module, string Label, PermissionTier Tier, bool Grantable, bool Baseline);

    public sealed record CatalogModule(string Key, string Label, IReadOnlyList<CatalogPermission> Permissions);

    /// <summary>
    /// Every permission the admin tree shows: its module, pt-BR label and tier (SPEC-iam-zero-trust §3.2).
    /// A permission missing here fails <c>IamCatalogTests</c>, so a new one cannot ship untiered.
    /// </summary>
    public static class IamCatalog
    {
        /// <summary>Held by every active user and never stored as a grant.</summary>
        public static readonly IReadOnlySet<string> Baseline = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            RbacPermissions.Users.ReadSelf,
            RbacPermissions.Users.UpdateSelf,
            RbacPermissions.Notifications.Read,
            RbacPermissions.Health.Read
        };

        private static readonly HashSet<string> Privileged = new(StringComparer.OrdinalIgnoreCase)
        {
            RbacPermissions.Iam.Manage,
            RbacPermissions.Users.Delete,
            RbacPermissions.Enterprise.Delete,
            RbacPermissions.Audit.CleanupRetention,
            RbacPermissions.Messaging.Redrive,
            RbacPermissions.EmployeeAbsences.Read,
            RbacPermissions.EmployeeAbsences.Create,
            RbacPermissions.EmployeeAbsences.Update,
            RbacPermissions.EmployeeAbsences.Delete,
            RbacPermissions.Payroll.Read,
            RbacPermissions.Payroll.Generate,
            RbacPermissions.Payroll.UpdateSettings,
            RbacPermissions.NFe.Cancel,
            RbacPermissions.Bills.Reissue
        };

        private static readonly HashSet<string> Sensitive = new(StringComparer.OrdinalIgnoreCase)
        {
            RbacPermissions.Iam.Read,
            RbacPermissions.Users.Create,
            RbacPermissions.Users.Update,
            RbacPermissions.Customers.Delete,
            RbacPermissions.Customers.CreditOverride,
            RbacPermissions.Bills.Settle,
            RbacPermissions.Orders.Refund,
            RbacPermissions.Orders.Delete,
            RbacPermissions.Products.ReadCost,
            RbacPermissions.BankReconciliation.Resolve,
            RbacPermissions.Notifications.Broadcast,
            RbacPermissions.Accounting.Export,
            RbacPermissions.Ai.Act
        };

        private static readonly Dictionary<string, string> ModuleLabels = new(StringComparer.OrdinalIgnoreCase)
        {
            ["accounting"] = "Contabilidade",
            ["ai"] = "Assistente de IA",
            ["audit"] = "Auditoria",
            ["bank-reconciliation"] = "Conciliação bancária",
            ["bills"] = "Boletos",
            ["cashflow"] = "Fluxo de caixa",
            ["customers"] = "Clientes",
            ["dashboard"] = "Painel",
            ["email"] = "E-mail",
            ["employee-absences"] = "Ausências",
            ["employee-vacations"] = "Férias",
            ["employees"] = "Funcionários",
            ["enterprise"] = "Empresa",
            ["health"] = "Saúde do sistema",
            ["iam"] = "Acesso (IAM)",
            ["messaging"] = "Mensageria",
            ["metrics"] = "Métricas",
            ["nfe"] = "NF-e",
            ["notifications"] = "Notificações",
            ["opportunities"] = "Oportunidades",
            ["orders"] = "Pedidos",
            ["payable-bills"] = "Contas a pagar",
            ["payroll"] = "Folha de pagamento",
            ["pdv"] = "PDV",
            ["products"] = "Produtos",
            ["quotes"] = "Orçamentos",
            ["reports"] = "Relatórios",
            ["sales"] = "Desempenho de vendas",
            ["storefront"] = "Loja Limen",
            ["suppliers"] = "Fornecedores",
            ["users"] = "Usuários"
        };

        private static readonly Dictionary<string, string> Labels = new(StringComparer.OrdinalIgnoreCase)
        {
            ["metrics.read"] = "Ver métricas",
            ["messaging.redrive"] = "Reprocessar mensagens",
            ["health.read"] = "Ver saúde do sistema",
            ["accounting.export"] = "Exportar para a contabilidade",
            ["ai.chat"] = "Conversar com o assistente",
            ["ai.act"] = "Deixar o assistente executar ações",
            ["notifications.read"] = "Ver notificações",
            ["notifications.broadcast"] = "Enviar avisos a todos",
            ["audit.read"] = "Ver trilha de auditoria",
            ["audit.retention.cleanup"] = "Limpar auditoria antiga",
            ["bank-reconciliation.read"] = "Ver conciliação",
            ["bank-reconciliation.import"] = "Importar extrato",
            ["bank-reconciliation.resolve"] = "Resolver divergências",
            ["dashboard.read"] = "Ver painel",
            ["bills.read"] = "Ver boletos",
            ["bills.generate"] = "Gerar boletos",
            ["bills.pdf.download"] = "Baixar PDF do boleto",
            ["bills.settle"] = "Dar baixa em boleto",
            ["bills.reissue"] = "Reemitir boleto",
            ["cashflow.forecast.read"] = "Ver previsão de caixa",
            ["cashflow.forecast.simulate"] = "Simular cenários",
            ["customers.read"] = "Ver clientes",
            ["customers.create"] = "Cadastrar clientes",
            ["customers.update"] = "Editar clientes",
            ["customers.delete"] = "Excluir clientes",
            ["customers.credit.override"] = "Liberar acima do limite de crédito",
            ["employees.read"] = "Ver funcionários",
            ["employees.create"] = "Cadastrar funcionários",
            ["employees.update"] = "Editar funcionários",
            ["employees.delete"] = "Excluir funcionários",
            ["employees.picture.upload"] = "Enviar foto do funcionário",
            ["employee-absences.read"] = "Ver ausências e atestados",
            ["employee-absences.create"] = "Registrar ausências",
            ["employee-absences.update"] = "Editar ausências",
            ["employee-absences.delete"] = "Excluir ausências",
            ["employee-vacations.read"] = "Ver férias",
            ["employee-vacations.schedule"] = "Agendar férias",
            ["email.manage"] = "Configurar e-mails",
            ["email.send"] = "Enviar e-mails",
            ["enterprise.read"] = "Ver empresas",
            ["enterprise.self.read"] = "Ver a própria empresa",
            ["enterprise.create"] = "Cadastrar empresas",
            ["enterprise.update"] = "Editar empresa",
            ["enterprise.delete"] = "Excluir empresa",
            ["enterprise.manage"] = "Administrar empresa",
            ["nfe.read"] = "Ver notas fiscais",
            ["nfe.issue"] = "Emitir NF-e",
            ["nfe.cancel"] = "Cancelar NF-e",
            ["nfe.settings.read"] = "Ver configuração fiscal",
            ["nfe.settings.update"] = "Editar configuração fiscal",
            ["nfe.certificate.manage"] = "Gerenciar certificado digital",
            ["nfe.certificate.read"] = "Ver certificado digital",
            ["opportunities.read"] = "Ver oportunidades",
            ["opportunities.feedback"] = "Avaliar oportunidades",
            ["opportunities.recompute"] = "Recalcular oportunidades",
            ["orders.read"] = "Ver pedidos",
            ["orders.create"] = "Criar pedidos",
            ["orders.update"] = "Editar pedidos",
            ["orders.refund"] = "Estornar pedidos",
            ["orders.delete"] = "Excluir pedidos",
            ["payable-bills.read"] = "Ver contas a pagar",
            ["payable-bills.create"] = "Lançar contas a pagar",
            ["payable-bills.update"] = "Editar contas a pagar",
            ["payable-bills.products.receive"] = "Receber mercadorias",
            ["payable-bills.refund"] = "Estornar contas a pagar",
            ["payable-bills.delete"] = "Excluir contas a pagar",
            ["payroll.read"] = "Ver folha de pagamento",
            ["payroll.generate"] = "Gerar folha de pagamento",
            ["payroll.settings.update"] = "Configurar folha de pagamento",
            ["sales.performance.read"] = "Ver desempenho da equipe",
            ["sales.performance.read.self"] = "Ver o próprio desempenho",
            ["sales.goals.manage"] = "Definir metas",
            ["products.read"] = "Ver produtos",
            ["products.create"] = "Cadastrar produtos",
            ["products.update"] = "Editar produtos",
            ["products.delete"] = "Excluir produtos",
            ["products.picture.upload"] = "Enviar fotos",
            ["products.picture.delete"] = "Excluir fotos",
            ["products.cost.read"] = "Ver custo",
            ["pdv.operate"] = "Operar o caixa",
            ["pdv.session.open"] = "Abrir caixa",
            ["pdv.session.close"] = "Fechar caixa",
            ["pdv.cash.movement"] = "Sangria e suprimento",
            ["pdv.discount.apply"] = "Aplicar desconto",
            ["pdv.discount.override"] = "Liberar desconto acima do limite",
            ["pdv.sale.cancel"] = "Cancelar venda",
            ["pdv.item.void"] = "Cancelar item",
            ["pdv.reports.read"] = "Ver relatórios do caixa",
            ["storefront.catalog.read"] = "Catálogo (serviço Limen)",
            ["storefront.checkout"] = "Checkout (serviço Limen)",
            ["storefront.orders.read"] = "Pedidos Limen (serviço)",
            ["storefront.orders.write"] = "Atualizar pedidos Limen (serviço)",
            ["storefront.manage"] = "Gerenciar a loja Limen",
            ["storefront.categories.read"] = "Ver categorias",
            ["storefront.categories.manage"] = "Gerenciar categorias",
            ["quotes.read"] = "Ver orçamentos",
            ["quotes.create"] = "Criar orçamentos",
            ["quotes.update"] = "Editar orçamentos",
            ["quotes.convert"] = "Converter em pedido",
            ["quotes.delete"] = "Excluir orçamentos",
            ["reports.read"] = "Ver relatórios",
            ["reports.generate"] = "Gerar relatórios",
            ["suppliers.read"] = "Ver fornecedores",
            ["suppliers.create"] = "Cadastrar fornecedores",
            ["suppliers.update"] = "Editar fornecedores",
            ["suppliers.delete"] = "Excluir fornecedores",
            ["users.read"] = "Ver usuários",
            ["users.self.read"] = "Ver o próprio perfil",
            ["users.create"] = "Cadastrar usuários",
            ["users.update"] = "Editar usuários",
            ["users.self.update"] = "Editar o próprio perfil",
            ["users.delete"] = "Excluir usuários",
            ["users.manage"] = "Administrar usuários",
            ["iam.read"] = "Ver acessos",
            ["iam.manage"] = "Conceder e revogar acessos"
        };

        public static IReadOnlyList<CatalogPermission> Permissions { get; } = Build();

        public static IReadOnlyDictionary<string, CatalogPermission> ByPermission { get; } =
            Permissions.ToDictionary(p => p.Permission, StringComparer.OrdinalIgnoreCase);

        public static IReadOnlyList<CatalogModule> Modules { get; } = Permissions
            .GroupBy(p => p.Module, StringComparer.OrdinalIgnoreCase)
            .Select(g => new CatalogModule(g.Key, ModuleLabels.GetValueOrDefault(g.Key, g.Key), g.ToList()))
            .OrderBy(m => m.Label, StringComparer.Create(new System.Globalization.CultureInfo("pt-BR"), false))
            .ToList();

        public static PermissionTier TierOf(string permission) =>
            ByPermission.TryGetValue(permission, out var entry) ? entry.Tier : PermissionTier.Standard;

        public static bool IsGrantable(string permission) =>
            ByPermission.TryGetValue(permission, out var entry) && entry.Grantable;

        /// <summary>The grantable permissions of a module key, as they are today: a module grant never covers later additions.</summary>
        public static IReadOnlyList<string> ExpandModule(string module) =>
            Permissions.Where(p => p.Grantable && string.Equals(p.Module, module, StringComparison.OrdinalIgnoreCase))
                .Select(p => p.Permission)
                .ToList();

        public static string ModuleOf(string permission)
        {
            var dot = permission.IndexOf('.');
            return dot < 0 ? permission : permission[..dot];
        }

        public static string? LabelOf(string permission) => Labels.GetValueOrDefault(permission);

        public static string? ModuleLabelOf(string module) => ModuleLabels.GetValueOrDefault(module);

        private static List<CatalogPermission> Build() =>
            RbacPermissions.All
                .Union(StorefrontTokenScope.ServicePermissions, StringComparer.OrdinalIgnoreCase)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Select(permission => new CatalogPermission(
                    permission,
                    ModuleOf(permission),
                    Labels.GetValueOrDefault(permission, permission),
                    Privileged.Contains(permission) ? PermissionTier.Privileged
                        : Sensitive.Contains(permission) ? PermissionTier.Sensitive
                        : PermissionTier.Standard,
                    Grantable: !Baseline.Contains(permission) && !StorefrontTokenScope.ServicePermissions.Contains(permission),
                    Baseline: Baseline.Contains(permission)))
                .ToList();
    }
}
