using EvangelionERPV2.Shared.Auditing;
using EvangelionERPV2.Shared.Entities;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using System.Security.Claims;

namespace EvangelionERPV2.Shared.Context
{
    public class AppDbContext : DbContext
    {
        private readonly IHttpContextAccessor? _httpContextAccessor;
        private readonly IAuditTrailEntryFactory? _auditTrailEntryFactory;

        public AppDbContext(
            DbContextOptions<AppDbContext> options,
            IHttpContextAccessor? httpContextAccessor = null,
            IAuditTrailEntryFactory? auditTrailEntryFactory = null) : base(options)
        {
            _httpContextAccessor = httpContextAccessor;
            _auditTrailEntryFactory = auditTrailEntryFactory;
        }

        /// <summary>
        /// The tenant every query is scoped to, or <see cref="Guid.Empty"/> when no request is in
        /// flight (a hosted service, a worker, or the login path, which runs before any tenant is
        /// known).
        /// </summary>
        /// <remarks>
        /// Read through <see cref="IHttpContextAccessor"/> on purpose. That service is a singleton
        /// backed by an AsyncLocal, so even though EF caches the model — and with it the context
        /// instance the query filters closed over — the value resolved here is always the one
        /// belonging to the request executing the query, not the one that happened to build the
        /// model. TenantQueryFilterTests pins exactly that.
        /// </remarks>
        public Guid AmbientEnterpriseId
        {
            get
            {
                var claimValue = _httpContextAccessor?.HttpContext?.User?.FindFirst(ClaimTypes.GroupSid)?.Value;

                return Guid.TryParse(claimValue, out var enterpriseId) ? enterpriseId : Guid.Empty;
            }
        }

        public DbSet<Customer> Customer { get; set; }
        public DbSet<Employee> Employee { get; set; }
        public DbSet<EmployeeAbsence> EmployeeAbsence { get; set; }
        public DbSet<Enterprise> Enterprise { get; set; }
        public DbSet<EnterprisePayrollSettings> EnterprisePayrollSettings { get; set; }
        public DbSet<EnterpriseFiscalSettings> EnterpriseFiscalSettings { get; set; }
        public DbSet<NFeNumberSequence> NFeNumberSequence { get; set; }
        public DbSet<Email> Email { get; set; }
        public DbSet<Bill> Bill { get; set; }
        public DbSet<BillNumberSequence> BillNumberSequence { get; set; }
        public DbSet<NFeDocument> NFeDocument { get; set; }
        public DbSet<Order> Order { get; set; }
        public DbSet<OrderedProduct> OrderedProduct { get; set; }
        public DbSet<Product> Product { get; set; }
        public DbSet<StockMovement> StockMovement { get; set; }
        public DbSet<PayableBill> PayableBill { get; set; }
        public DbSet<PayableBillProduct> PayableBillProduct { get; set; }
        public DbSet<PayrollBillDetail> PayrollBillDetail { get; set; }
        public DbSet<Supplier> Supplier { get; set; }
        public DbSet<Opportunity> Opportunity { get; set; }
        public DbSet<OpportunitySignal> OpportunitySignal { get; set; }
        public DbSet<OpportunityRecommendation> OpportunityRecommendation { get; set; }
        public DbSet<OpportunityFeedback> OpportunityFeedback { get; set; }
        public DbSet<OpportunityRunLog> OpportunityRunLog { get; set; }
        public DbSet<ForecastSimulationLog> ForecastSimulationLog { get; set; }
        public DbSet<RefreshToken> RefreshToken { get; set; }
        public DbSet<PasswordResetToken> PasswordResetToken { get; set; }
        public DbSet<User> User { get; set; }
        public DbSet<AuditTrail> AuditTrails { get; set; }

        protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
        {
            base.ConfigureConventions(configurationBuilder);

            // Money columns are decimal(18,2). Without this, EF only infers a provider default
            // and warns that values may be silently truncated. Properties that declare their own
            // [Column(TypeName = ...)] — the payroll rates at decimal(5,4) — still win over this.
            configurationBuilder.Properties<decimal>().HavePrecision(18, 2);
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            modelBuilder.Entity<Enterprise>()
                .Property(enterprise => enterprise.Currency)
                .HasMaxLength(3)
                .HasDefaultValue("BRL");

            modelBuilder.Entity<Enterprise>()
                .Property(enterprise => enterprise.CurrentBalance)
                .HasDefaultValue(0m);

            modelBuilder.Entity<Bill>()
                .ToTable("Boleto");

            // One live boleto per order, but not one boleto per order for all time: a cancelled
            // title drops out of the index so a reissue can supersede it instead of the API having
            // to mutate a document a payer may already be holding. Filtered on Status rather than
            // IsActive because IsActive is nullable — legacy rows carry NULL, which SQL Server
            // excludes from a "[IsActive] = 1" filter and would silently stop guarding them.
            modelBuilder.Entity<Bill>()
                .HasIndex(bill => bill.OrderId)
                .HasFilter($"[Status] <> {(int)BillStatus.Cancelled}")
                .IsUnique();

            // Deliberately NOT filtered on IsActive — see BillNumberSequence. A soft-deleted counter
            // that let a replacement row restart at the range floor would re-issue NossoNumero
            // values already handed to payers.
            modelBuilder.Entity<BillNumberSequence>()
                .HasOne(sequence => sequence.Enterprise)
                .WithMany()
                .HasForeignKey(sequence => sequence.EnterpriseId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<Email>()
                .HasOne(email => email.Enterprise)
                .WithMany()
                .HasForeignKey(email => email.EnterpriseId)
                .OnDelete(DeleteBehavior.SetNull);

            modelBuilder.Entity<NFeDocument>()
                .Property(document => document.AccessKey)
                .HasMaxLength(44);

            modelBuilder.Entity<PayableBill>()
                .HasMany(payableBill => payableBill.Items)
                .WithOne(item => item.PayableBill)
                .HasForeignKey(item => item.PayableBillId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<PayableBillProduct>()
                .HasOne(item => item.Product)
                .WithMany(product => product.PayableBillProducts)
                .HasForeignKey(item => item.ProductId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<PayableBillProduct>()
                .HasIndex(item => new { item.PayableBillId, item.ProductId })
                .HasFilter("[IsActive] = 1")
                .IsUnique();

            // Restrict, not the Cascade EF would infer from a required FK: a ledger row whose
            // product row vanished stops being reconstructable, which is the one thing this table
            // exists to prevent. Products are soft-deleted anyway, so nothing legitimate trips it.
            modelBuilder.Entity<StockMovement>()
                .HasOne(movement => movement.Product)
                .WithMany()
                .HasForeignKey(movement => movement.ProductId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<PayrollBillDetail>()
                .HasOne(detail => detail.Employee)
                .WithMany()
                .HasForeignKey(detail => detail.EmployeeId)
                .OnDelete(DeleteBehavior.Restrict);

            // Restrict for the same reason as PayrollBillDetail: absences are the evidence behind
            // a payroll amount, so they must outlive any attempt to hard-delete the employee.
            modelBuilder.Entity<EmployeeAbsence>()
                .HasOne(absence => absence.Employee)
                .WithMany()
                .HasForeignKey(absence => absence.EmployeeId)
                .OnDelete(DeleteBehavior.Restrict);

            // Transactional guard against concurrent GeneratePayroll calls double-billing the
            // same employee for the same competência. Filtered so soft-deleted rows do not block
            // a regeneration.
            modelBuilder.Entity<PayrollBillDetail>()
                .HasIndex(detail => new { detail.ReferenceYear, detail.ReferenceMonth, detail.EmployeeId })
                .HasFilter("[IsActive] = 1")
                .IsUnique();

            // Soft-deleted settings rows must not block the create branch of UpsertSettingsAsync,
            // which only looks up rows with IsActive = 1.
            modelBuilder.Entity<EnterprisePayrollSettings>()
                .HasIndex(settings => settings.EnterpriseId)
                .HasFilter("[IsActive] = 1")
                .IsUnique();

            // Same reasoning as payroll: soft-deleted rows must not block the create branch of
            // UpsertSettingsAsync, which only looks up rows with IsActive = 1.
            modelBuilder.Entity<EnterpriseFiscalSettings>()
                .HasIndex(settings => settings.EnterpriseId)
                .HasFilter("[IsActive] = 1")
                .IsUnique();

            // Deliberately NOT filtered on IsActive — see NFeNumberSequence. A soft-deleted
            // counter that let a replacement row restart at 1 would re-issue nNF values already
            // transmitted to SEFAZ.
            modelBuilder.Entity<NFeNumberSequence>()
                .HasOne(sequence => sequence.Enterprise)
                .WithMany()
                .HasForeignKey(sequence => sequence.EnterpriseId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<PasswordResetToken>()
                .HasOne(token => token.User)
                .WithMany()
                .HasForeignKey(token => token.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<PasswordResetToken>()
                .HasIndex(token => token.UserId)
                .HasDatabaseName("IX_PasswordResetToken_UserId_Active")
                .IsUnique()
                .HasFilter("[IsActive] = 1");

            modelBuilder.Entity<RefreshToken>()
                .HasIndex(token => token.UserId)
                .HasDatabaseName("IX_RefreshToken_UserId_Active")
                .IsUnique()
                .HasFilter("[IsActive] = 1");

            modelBuilder.Entity<User>()
                .Property<string>("NormalizedActiveUserName")
                .HasMaxLength(450)
                .HasComputedColumnSql(
                    "CASE WHEN [IsActive] = 1 THEN CONVERT(nvarchar(450), UPPER(LTRIM(RTRIM([UserName])))) END",
                    stored: true);

            modelBuilder.Entity<User>()
                .Property<string>("NormalizedActiveEmail")
                .HasMaxLength(450)
                .HasComputedColumnSql(
                    "CASE WHEN [IsActive] = 1 THEN CONVERT(nvarchar(450), UPPER(LTRIM(RTRIM([Email])))) END",
                    stored: true);

            modelBuilder.Entity<User>()
                .HasIndex("EnterpriseId", "NormalizedActiveUserName")
                .HasDatabaseName("IX_User_EnterpriseId_NormalizedActiveUserName_Active")
                .IsUnique()
                .HasFilter("[IsActive] = 1 AND [EnterpriseId] IS NOT NULL");

            // Filter cannot reference [NormalizedActiveEmail]: SQL Server rejects computed
            // columns in filtered-index predicates. [Email] is NOT NULL, so [IsActive] = 1
            // alone already excludes every row where the computed value is NULL.
            modelBuilder.Entity<User>()
                .HasIndex("NormalizedActiveEmail")
                .HasDatabaseName("IX_User_NormalizedActiveEmail_Active")
                .IsUnique()
                .HasFilter("[IsActive] = 1");

            modelBuilder.Entity<Opportunity>()
                .Property(opportunity => opportunity.Type)
                .HasMaxLength(64)
                .IsRequired();

            modelBuilder.Entity<Opportunity>()
                .Property(opportunity => opportunity.Status)
                .HasMaxLength(32)
                .IsRequired();

            modelBuilder.Entity<Opportunity>()
                .Property(opportunity => opportunity.Title)
                .HasMaxLength(256)
                .IsRequired();

            modelBuilder.Entity<Opportunity>()
                .Property(opportunity => opportunity.SourceRule)
                .HasMaxLength(128)
                .IsRequired();

            modelBuilder.Entity<Opportunity>()
                .Property(opportunity => opportunity.SourceModel)
                .HasMaxLength(128)
                .IsRequired();

            modelBuilder.Entity<Opportunity>()
                .Property(opportunity => opportunity.Fingerprint)
                .HasMaxLength(256)
                .IsRequired();

            modelBuilder.Entity<Opportunity>()
                .HasMany(opportunity => opportunity.Signals)
                .WithOne(signal => signal.Opportunity)
                .HasForeignKey(signal => signal.OpportunityId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<Opportunity>()
                .HasMany(opportunity => opportunity.Recommendations)
                .WithOne(recommendation => recommendation.Opportunity)
                .HasForeignKey(recommendation => recommendation.OpportunityId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<Opportunity>()
                .HasMany(opportunity => opportunity.Feedbacks)
                .WithOne(feedback => feedback.Opportunity)
                .HasForeignKey(feedback => feedback.OpportunityId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<OpportunitySignal>()
                .Property(signal => signal.SignalType)
                .HasMaxLength(64)
                .IsRequired();

            modelBuilder.Entity<OpportunitySignal>()
                .Property(signal => signal.SignalKey)
                .HasMaxLength(128)
                .IsRequired();

            modelBuilder.Entity<OpportunitySignal>()
                .Property(signal => signal.Unit)
                .HasMaxLength(32)
                .IsRequired();

            modelBuilder.Entity<OpportunitySignal>()
                .Property(signal => signal.SourceEntity)
                .HasMaxLength(64)
                .IsRequired();

            modelBuilder.Entity<OpportunitySignal>()
                .Property(signal => signal.SourceEntityId)
                .HasMaxLength(128)
                .IsRequired();

            modelBuilder.Entity<OpportunityRecommendation>()
                .Property(recommendation => recommendation.ActionTitle)
                .HasMaxLength(160)
                .IsRequired();

            modelBuilder.Entity<OpportunityRecommendation>()
                .Property(recommendation => recommendation.PriorityLabel)
                .HasMaxLength(16)
                .IsRequired();

            modelBuilder.Entity<OpportunityFeedback>()
                .Property(feedback => feedback.Status)
                .HasMaxLength(32)
                .IsRequired();

            modelBuilder.Entity<OpportunityFeedback>()
                .HasOne(feedback => feedback.User)
                .WithMany()
                .HasForeignKey(feedback => feedback.UserId)
                .OnDelete(DeleteBehavior.SetNull);

            modelBuilder.Entity<OpportunityRunLog>()
                .Property(runLog => runLog.TriggerType)
                .HasMaxLength(32)
                .IsRequired();

            modelBuilder.Entity<OpportunityRunLog>()
                .Property(runLog => runLog.Status)
                .HasMaxLength(32)
                .IsRequired();

            modelBuilder.Entity<OpportunityRunLog>()
                .Property(runLog => runLog.CorrelationId)
                .HasMaxLength(128)
                .IsRequired();

            modelBuilder.Entity<OpportunityRunLog>()
                .HasOne(runLog => runLog.User)
                .WithMany()
                .HasForeignKey(runLog => runLog.RequestedByUserId)
                .OnDelete(DeleteBehavior.SetNull);

            modelBuilder.Entity<AuditTrail>()
                .Property(audit => audit.Action)
                .HasMaxLength(16)
                .IsRequired();

            modelBuilder.Entity<AuditTrail>()
                .Property(audit => audit.EntityName)
                .HasMaxLength(128)
                .IsRequired();

            modelBuilder.Entity<AuditTrail>()
                .Property(audit => audit.ChangesJson)
                .IsRequired();

            modelBuilder.Entity<AuditTrail>()
                .HasOne(audit => audit.User)
                .WithMany()
                .HasForeignKey(audit => audit.UserId)
                .OnDelete(DeleteBehavior.SetNull);

            ApplyTenantQueryFilters(modelBuilder);
        }

        /// <summary>
        /// Scopes every tenant-owned entity to <see cref="AmbientEnterpriseId"/> in the model itself.
        /// </summary>
        /// <remarks>
        /// Isolation used to be hand-written in every controller, with regression tests as the only
        /// safety net — which means a new controller is isolated exactly as long as someone remembers.
        /// Applied as a convention rather than entity by entity so that a module added later inherits
        /// it without opting in, which is the whole point of landing this before the controller
        /// surface grows. The hand-written filters stay: redundant, harmless, second belt.
        ///
        /// Guid.Empty disables the filter instead of hiding everything. Out-of-request work is
        /// legitimately cross-tenant — the NFe reconciliation sweeper, the email workers, the login
        /// lookup that runs before a tenant is known — and a filter that returned nothing there would
        /// break them silently at runtime rather than loudly at compile time. Inside a request the
        /// value is never empty: ActiveTenantEnforcementMiddleware 401s an authenticated request whose
        /// enterprise claim is missing before any handler runs.
        ///
        /// A row whose EnterpriseId is NULL is invisible to every request. That matches what the
        /// hand-written filters already did — they all compare for equality — so this changes no API
        /// behaviour, and a row belonging to nobody must not surface for everybody.
        /// </remarks>
        private void ApplyTenantQueryFilters(ModelBuilder modelBuilder)
        {
            foreach (var entityType in modelBuilder.Model.GetEntityTypes())
            {
                // Derived types in a hierarchy inherit the root's filter; EF rejects one of their own.
                if (entityType.BaseType != null || entityType.ClrType == null)
                    continue;

                // Enterprise is the tenant itself, so its own key is what has to match.
                var tenantProperty = entityType.ClrType == typeof(Enterprise)
                    ? nameof(BaseEntity.Id)
                    : entityType.ClrType.GetProperty(TenantPropertyName) == null ? null : TenantPropertyName;

                if (tenantProperty == null)
                    continue;

                ApplyTenantQueryFilterMethod
                    .MakeGenericMethod(entityType.ClrType)
                    .Invoke(this, [modelBuilder, tenantProperty]);
            }
        }

        private const string TenantPropertyName = "EnterpriseId";

        private static readonly System.Reflection.MethodInfo ApplyTenantQueryFilterMethod =
            typeof(AppDbContext).GetMethod(
                nameof(ApplyTenantQueryFilter),
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;

        /// <summary>
        /// The filter itself, generic so the lambda is a real typed expression rather than a
        /// hand-assembled tree. <c>EF.Property</c> reads the column whether it is declared
        /// <c>Guid</c> or <c>Guid?</c>, which the entities are inconsistent about.
        /// </summary>
        private void ApplyTenantQueryFilter<TEntity>(ModelBuilder modelBuilder, string tenantProperty)
            where TEntity : class
        {
            modelBuilder.Entity<TEntity>().HasQueryFilter(entity =>
                AmbientEnterpriseId == Guid.Empty
                || EF.Property<Guid?>(entity, tenantProperty) == AmbientEnterpriseId);
        }

        public override int SaveChanges()
        {
            return SaveChanges(acceptAllChangesOnSuccess: true);
        }

        public override int SaveChanges(bool acceptAllChangesOnSuccess)
        {
            RestoreOriginalValuesForAudit();
            AppendAuditTrails();
            return base.SaveChanges(acceptAllChangesOnSuccess);
        }

        public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            return SaveChangesAsync(acceptAllChangesOnSuccess: true, cancellationToken);
        }

        public override async Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
        {
            await RestoreOriginalValuesForAuditAsync(cancellationToken);
            AppendAuditTrails();
            return await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
        }

        /// <summary>
        /// Reloads database original values for audited entities that were attached from a detached
        /// instance, so the audit trail can tell what actually changed.
        /// </summary>
        /// <remarks>
        /// Every read in <c>RepositoryBase</c> is <c>AsNoTracking</c>, so an update flow is
        /// read-detached → mutate → <c>Update</c>. For an untracked entity that lands on
        /// <c>DbSet.Update</c>, EF seeds OriginalValues from the very instance being saved: every
        /// property is marked modified, yet <c>OriginalValue == CurrentValue</c> for all of them.
        /// <see cref="Auditing.IAuditTrailEntryFactory"/> skips unchanged properties, so the change
        /// set came out empty and <b>no EDIT row was written at all</b> — for any audited entity,
        /// not just fiscal ones. One extra SELECT per audited modified entity buys a real trail;
        /// non-audited entities are untouched, which is why this is not a blanket cost.
        /// </remarks>
        private async Task RestoreOriginalValuesForAuditAsync(CancellationToken cancellationToken)
        {
            foreach (var entry in CollectAuditedEntriesNeedingOriginalValues())
            {
                var databaseValues = await entry.GetDatabaseValuesAsync(cancellationToken);
                if (databaseValues != null)
                    entry.OriginalValues.SetValues(databaseValues);
            }
        }

        private void RestoreOriginalValuesForAudit()
        {
            foreach (var entry in CollectAuditedEntriesNeedingOriginalValues())
            {
                var databaseValues = entry.GetDatabaseValues();
                if (databaseValues != null)
                    entry.OriginalValues.SetValues(databaseValues);
            }
        }

        private List<EntityEntry<BaseEntity>> CollectAuditedEntriesNeedingOriginalValues()
        {
            ChangeTracker.DetectChanges();

            return ChangeTracker.Entries<BaseEntity>()
                .Where(entry => entry.State == EntityState.Modified
                                && AuditedEntities.Contains(entry.Metadata.ClrType)
                                // Nothing to recover when EF already holds real originals from a
                                // tracked read; only the attach-from-detached case is degenerate.
                                && entry.Properties.All(property =>
                                    !property.IsModified || Equals(property.OriginalValue, property.CurrentValue)))
                .ToList();
        }

        private void AppendAuditTrails()
        {
            var now = DateTime.UtcNow;

            ChangeTracker.DetectChanges();

            var auditableEntries = ChangeTracker.Entries<BaseEntity>()
                .Where(entry => entry.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)
                .ToList();

            if (auditableEntries.Count == 0)
                return;

            var userId = ResolveCurrentUserId();

            if (_auditTrailEntryFactory == null)
                throw new InvalidOperationException("IAuditTrailEntryFactory is not registered. Configure AuditTrailIoC before using AppDbContext.");

            var enterpriseId = ResolveCurrentEnterpriseId();
            var pendingAuditEntries = _auditTrailEntryFactory.Create(auditableEntries, userId, enterpriseId, now);

            if (pendingAuditEntries.Count == 0)
                return;

            AuditTrails.AddRange(pendingAuditEntries);
        }

        private Guid? ResolveCurrentUserId()
        {
            var user = _httpContextAccessor?.HttpContext?.User;
            if (user?.Identity?.IsAuthenticated != true)
                return null;

            var idClaim = user.FindFirst(ClaimTypes.Sid)?.Value
                          ?? user.FindFirst("uid")?.Value
                          ?? user.FindFirst("sub")?.Value;

            if (!string.IsNullOrWhiteSpace(idClaim) && Guid.TryParse(idClaim, out var parsedId))
                return parsedId;

            var userName = user.FindFirst(ClaimTypes.NameIdentifier)?.Value
                           ?? user.Identity?.Name;

            if (string.IsNullOrWhiteSpace(userName))
                return null;

            var trackedUserId = ChangeTracker.Entries<User>()
                .Where(entry => entry.State is not EntityState.Deleted)
                .Select(entry => entry.Entity)
                .Where(entity => entity.UserName == userName)
                .Select(entity => (Guid?)entity.Id)
                .FirstOrDefault();

            if (trackedUserId.HasValue)
                return trackedUserId.Value;

            return User.AsNoTracking()
                .Where(entity => entity.UserName == userName)
                .Select(entity => (Guid?)entity.Id)
                .FirstOrDefault();
        }

        private Guid? ResolveCurrentEnterpriseId()
        {
            var user = _httpContextAccessor?.HttpContext?.User;
            if (user?.Identity?.IsAuthenticated != true)
                return null;

            var enterpriseClaim = user.FindFirst(ClaimTypes.GroupSid)?.Value
                                  ?? user.FindFirst("enterprise_id")?.Value
                                  ?? user.FindFirst("tenant_id")?.Value;

            return !string.IsNullOrWhiteSpace(enterpriseClaim) &&
                   Guid.TryParse(enterpriseClaim, out var enterpriseId) &&
                   enterpriseId != Guid.Empty
                ? enterpriseId
                : null;
        }

    }
}
