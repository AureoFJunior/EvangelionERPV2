using EvangelionERPV2.NFeModule.Application.Configs;
using EvangelionERPV2.NFeModule.Application.Interface;
using EvangelionERPV2.NFeModule.Application.Providers;
using EvangelionERPV2.NFeModule.Domain.Interface;
using EvangelionERPV2.NFeModule.Infra.Providers;
using EvangelionERPV2.NFeModule.Infra.Xml;
using EvangelionERPV2.Shared.Entities;
using EvangelionERPV2.Shared.Exceptions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Serilog;
using System.Globalization;

namespace EvangelionERPV2.NFeModule.Application.Services
{
    public class NFeService : INFeService<NFeDocument>, IDisposable
    {
        private const int IssuanceLockPoolSize = 256;

        /// <summary>tpEmis 1 — emissão normal.</summary>
        private const int NormalEmission = 1;

        /// <summary>tpEmis 9 — contingência off-line NFC-e. The only contingency mode supported.</summary>
        private const int OfflineNFCeEmission = 9;

        private const byte ProductionEnvironment = 1;

        private static readonly TimeSpan IssuanceClaimPollDelay = TimeSpan.FromMilliseconds(200);
        private static readonly TimeSpan IssuanceClaimPollTimeout = TimeSpan.FromSeconds(15);
        private static readonly SemaphoreSlim[] IssuanceLocks = CreateIssuanceLocks();

        private readonly EvangelionERPV2.Shared.Repositories.IRepository<NFeDocument> _nfeRepository;
        private readonly INFeRepository<NFeDocument> _nfeRepositoryCustom;
        private readonly EvangelionERPV2.Shared.Repositories.IRepository<Order> _orderRepository;
        private readonly EvangelionERPV2.Shared.Repositories.IRepository<Customer> _customerRepository;
        private readonly EvangelionERPV2.Shared.Repositories.IRepository<Enterprise> _enterpriseRepository;
        private readonly INFeProvider _provider;
        private readonly INFeFiscalSettingsService _fiscalSettingsService;
        private readonly NFeSettings _settings;
        private bool disposed;

        public NFeService(
            EvangelionERPV2.Shared.Repositories.IRepository<NFeDocument> nfeRepository,
            INFeRepository<NFeDocument> nfeRepositoryCustom,
            EvangelionERPV2.Shared.Repositories.IRepository<Order> orderRepository,
            EvangelionERPV2.Shared.Repositories.IRepository<Customer> customerRepository,
            EvangelionERPV2.Shared.Repositories.IRepository<Enterprise> enterpriseRepository,
            INFeProvider provider,
            INFeFiscalSettingsService fiscalSettingsService,
            IOptions<NFeSettings> settings)
        {
            _nfeRepository = nfeRepository;
            _nfeRepositoryCustom = nfeRepositoryCustom;
            _orderRepository = orderRepository;
            _customerRepository = customerRepository;
            _enterpriseRepository = enterpriseRepository;
            _provider = provider;
            _fiscalSettingsService = fiscalSettingsService;
            _settings = settings?.Value ?? new NFeSettings();
        }

        public async Task<NFeDocument?> GetByOrderIdAsync(Guid orderId, Guid enterpriseId, NFeDocumentType? type = null)
        {
            if (orderId == Guid.Empty)
                return null;

            if (!await IsOrderWithinEnterpriseAsync(orderId, enterpriseId))
                return null;

            return await _nfeRepositoryCustom.GetByOrderIdAsync(orderId, type);
        }

        public async Task<NFeDocument?> IssueAsync(
            Guid orderId,
            Guid enterpriseId,
            NFeDocumentType type = NFeDocumentType.NFe,
            NFeContingency? contingency = null)
        {
            if (!_settings.Enabled)
            {
                Log.Logger.Information("NFe/NFCe issuance skipped: disabled in settings.");
                return null;
            }

            if (orderId == Guid.Empty)
                throw new InsertDatabaseException("Order Id is invalid for NFe issuance.");

            var order = await GetScopedOrderAsync(orderId, enterpriseId);
            if (order == null)
                throw new NotFoundDatabaseException($"{nameof(Order)} was not found.");

            var fiscalSettings = await _fiscalSettingsService.GetSettingsAsync(enterpriseId);

            // The tenant's own switches, AND-ed with the global one above. Read before the claim so
            // a shop that has issuance turned off never leaves a Pending document behind, and
            // checked here rather than only at the till's endpoint: NFeCertificateService sets
            // Enabled = false when a certificate is removed, and until this existed that stopped
            // nothing. A tenant that never configured a profile is refused here too — it was
            // already refused a few lines further on, for having no CNPJ.
            if (!fiscalSettings.Enabled || (type == NFeDocumentType.NFCe && !fiscalSettings.NFCeEnabled))
            {
                Log.Logger.Information(
                    "NFe/NFCe issuance skipped: {Type} is not enabled for enterprise {EnterpriseId}.",
                    type,
                    enterpriseId);

                return null;
            }

            var issuanceLock = GetIssuanceLock(orderId, type);
            await issuanceLock.WaitAsync();
            try
            {
                var existing = await _nfeRepositoryCustom.GetByOrderIdAsync(orderId, type);
                NFeDocument pendingDocument;
                if (existing != null)
                {
                    if (existing.Status == NFeStatus.Pending)
                        return await WaitForFinalizedIssuanceAsync(orderId, type, existing);

                    if (existing.Status != NFeStatus.Error)
                        return existing;

                    pendingDocument = ResetErroredDocumentForRetry(existing);

                    // Backfills rows created before these columns existed.
                    pendingDocument.EnterpriseId = enterpriseId;
                    pendingDocument.Model = NFeDocument.ModelFor(type);

                    _nfeRepository.Update(pendingDocument);
                    await _nfeRepository.CommitAsync();
                }
                else
                {
                    var now = DateTime.UtcNow;
                    pendingDocument = new NFeDocument
                    {
                        Id = Guid.NewGuid(),
                        OrderId = order.Id,
                        // GetScopedOrderAsync only returns an order whose EnterpriseId equals
                        // the caller's, so this is the verified tenant, not a hopeful copy.
                        EnterpriseId = enterpriseId,
                        Type = type,
                        Model = NFeDocument.ModelFor(type),
                        Status = NFeStatus.Pending,
                        AccessKey = string.Empty,
                        Series = string.Empty,
                        Number = string.Empty,
                        Environment = string.Empty,
                        Protocol = string.Empty,
                        IssuedAt = null,
                        TotalValue = order.TotalValue,
                        XmlContent = string.Empty,
                        CreatedAt = now,
                        UpdatedAt = now,
                        IsActive = true
                    };

                    await _nfeRepository.CreateAsync(pendingDocument);
                    try
                    {
                        await _nfeRepository.CommitAsync();
                    }
                    catch (DbUpdateException commitException)
                    {
                        var concurrentDocument = await WaitForFinalizedIssuanceAsync(orderId, type);
                        if (concurrentDocument != null)
                        {
                            Log.Logger.Warning(
                                commitException,
                                "NFe issuance claim duplicate detected for order {OrderId} and type {Type}. Returning existing document {DocumentId}.",
                                orderId,
                                type,
                                concurrentDocument.Id);

                            return concurrentDocument;
                        }

                        throw;
                    }
                }

                var enterprise = order.EnterpriseId.HasValue ? await _enterpriseRepository.GetByIdAsync(order.EnterpriseId.Value) : null;
                var customer = order.CustomerId.HasValue ? await _customerRepository.GetByIdAsync(order.CustomerId.Value) : null;

                try
                {
                    // Steps 2-5 of the transmit-safe ordering (docs/nfe/HANDOFF.md §4). Both commit
                    // before the provider is called, and both are inside this try so that a failure
                    // here — where nothing can have been transmitted — still lands as Error.
                    var tipoEmissao = contingency is null ? NormalEmission : OfflineNFCeEmission;

                    if (contingency is not null && type != NFeDocumentType.NFCe)
                        throw new InsertDatabaseException("Off-line contingency (tpEmis 9) exists only for NFC-e.");

                    await AssignNumberAndAccessKeyAsync(pendingDocument, fiscalSettings, type, tipoEmissao);

                    // Recorded before transmission like every other identity field: a contingency
                    // note that is printed and handed over still owes SEFAZ a transmission, and this
                    // is what a later sweep reads to find the ones that never made it.
                    pendingDocument.ContingencyEnteredAt = contingency?.EnteredAt;
                    pendingDocument.ContingencyJustification = contingency?.Justification;
                    await MarkTransmittedAsync(pendingDocument);

                    var result = await _provider.IssueAsync(new NFeIssueContext
                    {
                        Order = order,
                        Items = order.OrderedProduct?.ToList() ?? [],
                        FiscalSettings = fiscalSettings,
                        Enterprise = enterprise,
                        Customer = customer,
                        Type = type,
                        AccessKey = pendingDocument.AccessKey,
                        Number = pendingDocument.Number,
                        Series = pendingDocument.Series,
                        TipoEmissao = pendingDocument.TipoEmissao,
                        ContingencyEnteredAt = pendingDocument.ContingencyEnteredAt,
                        ContingencyJustification = pendingDocument.ContingencyJustification
                    });

                    // The identity fields are NOT adopted from the result. The chave, number and
                    // series were allocated and committed before transmission precisely so that a
                    // timed-out request can be reconciled against them; taking the provider's word
                    // for them afterwards would put the recovery key back under SEFAZ's control.
                    //
                    // Everything else goes through the one adoption path the reconciler and the
                    // contingency sweeper use. A hand-copied version stood here and had already
                    // drifted from it in two ways that mattered: it dropped the consumer QR, so an
                    // NFC-e authorised at a counter had nothing for the customer to scan, and it
                    // ignored NeverTransmitted, so a failure that provably never reached the wire
                    // kept its write-ahead marker — burning the allocated number on the next
                    // attempt and hiding the one state the till reads to fall back to contingency.
                    ApplyOutcome(pendingDocument, result);

                    pendingDocument.ReceiptNumber = result.ReceiptNumber;
                    pendingDocument.TotalValue = result.TotalValue;

                    // Issuance always stamps an attempt time; a later consult may answer without one.
                    pendingDocument.IssuedAt ??= DateTime.UtcNow;

                    _nfeRepository.Update(pendingDocument);
                    try
                    {
                        await _nfeRepository.CommitAsync();
                        return pendingDocument;
                    }
                    catch (DbUpdateException commitException)
                    {
                        var concurrentDocument = await WaitForFinalizedIssuanceAsync(orderId, type);
                        if (concurrentDocument != null)
                        {
                            Log.Logger.Warning(
                                commitException,
                                "NFe issuance finalize duplicate detected for order {OrderId} and type {Type}. Returning existing document {DocumentId}.",
                                orderId,
                                type,
                                concurrentDocument.Id);

                            return concurrentDocument;
                        }

                        throw;
                    }
                }
                catch
                {
                    // Only downgrade to Error while the write-ahead marker says nothing can have
                    // been transmitted. Once TransmittedAtUtc is set the outcome is UNKNOWN, not
                    // failed — the bytes may have reached SEFAZ. Marking it Error would make
                    // ResetErroredDocumentForRetry legal on the next attempt, and that clears the
                    // access key and the marker, i.e. exactly the two things that make a duplicate
                    // emission detectable. Leave it Pending and let the reconciler consult SEFAZ
                    // and adopt the answer. See docs/nfe/HANDOFF.md §4.
                    if (!pendingDocument.IsOutcomeUnknown)
                    {
                        pendingDocument.Status = NFeStatus.Error;
                        pendingDocument.UpdatedAt = DateTime.UtcNow;
                        _nfeRepository.Update(pendingDocument);

                        try
                        {
                            await _nfeRepository.CommitAsync();
                        }
                        catch (DbUpdateException errorCommitException)
                        {
                            Log.Logger.Warning(
                                errorCommitException,
                                "NFe issuance failure could not be persisted for order {OrderId} and type {Type}.",
                                orderId,
                                type);
                            throw;
                        }
                    }
                    else
                    {
                        Log.Logger.Error(
                            "NFe issuance failed after transmission for order {OrderId} and type {Type}. Document {DocumentId} stays Pending with an unknown outcome; the reconciler owns it.",
                            orderId,
                            type,
                            pendingDocument.Id);
                    }

                    throw;
                }
            }
            finally
            {
                issuanceLock.Release();
            }
        }

        /// <summary>
        /// Reads the document and, when SEFAZ is the only thing that can settle its state, asks.
        /// </summary>
        /// <remarks>
        /// A pure database read was the old behaviour and it was misleading: the one case where a
        /// caller actually needs a consult is a document whose outcome is unknown, and that is
        /// exactly the case a local read cannot answer. Terminal documents are still answered from
        /// the database — SEFAZ's verdict on them cannot change, so a round trip would buy nothing.
        /// </remarks>
        public async Task<NFeDocument?> ConsultAsync(string accessKey, Guid enterpriseId)
        {
            if (string.IsNullOrWhiteSpace(accessKey))
                return null;

            var document = await GetScopedDocumentByAccessKeyAsync(accessKey, enterpriseId);
            if (document == null || !document.IsOutcomeUnknown || !_settings.Enabled)
                return document;

            var fiscalSettings = await _fiscalSettingsService.GetSettingsAsync(enterpriseId);
            var result = await _provider.ConsultAsync(document.AccessKey, fiscalSettings);

            if (result.Status == NFeStatus.Pending)
                return document;

            ApplyOutcome(document, result);

            _nfeRepository.Update(document);
            await _nfeRepository.CommitAsync();

            return document;
        }

        /// <summary>
        /// Sends event 110111 and adopts SEFAZ's answer.
        /// </summary>
        /// <remarks>
        /// This used to write Status = Cancelled locally and invent a "CANCEL-{timestamp}" protocol
        /// without contacting anybody, which meant the database said cancelled while the note stayed
        /// valid at SEFAZ and the tax liability stayed real. Nothing is written now unless the event
        /// was homologated, and the cancellation window comes from the tenant's profile rather than
        /// a constant, because it differs between NFe and NFCe and is a per-UF rule.
        /// </remarks>
        public async Task<NFeDocument?> CancelAsync(string accessKey, Guid enterpriseId, string reason = "")
        {
            if (string.IsNullOrWhiteSpace(accessKey))
                return null;

            var document = await GetScopedDocumentByAccessKeyAsync(accessKey, enterpriseId);
            if (document == null)
                return null;

            if (document.Status == NFeStatus.Cancelled)
                return document;

            if (document.Status != NFeStatus.Authorized)
                throw new InsertDatabaseException("Only an authorized document can be cancelled.");

            var fiscalSettings = await _fiscalSettingsService.GetSettingsAsync(enterpriseId);

            var windowMinutes = document.Type == NFeDocumentType.NFCe
                ? fiscalSettings.NFCeCancellationWindowMinutes
                : fiscalSettings.NFeCancellationWindowMinutes;

            // Measured from dhRecbto, which is SEFAZ's clock, not ours. AuthorizedAtUtc is null on
            // documents authorized before that column existed; those fall back to IssuedAt.
            var authorizedAt = document.AuthorizedAtUtc ?? document.IssuedAt;
            if (authorizedAt.HasValue && DateTime.UtcNow > authorizedAt.Value.AddMinutes(windowMinutes))
                throw new InsertDatabaseException("The cancellation window for this document has closed.");

            // Written before the round trip for the same reason TransmittedAtUtc is: a cancellation
            // whose answer is lost must be recognisable as attempted rather than as never sent.
            document.CancelRequestedAt = DateTime.UtcNow;
            document.CancelReason = reason ?? string.Empty;
            document.UpdatedAt = DateTime.UtcNow;
            _nfeRepository.Update(document);
            await _nfeRepository.CommitAsync();

            var result = await _provider.CancelAsync(
                document.AccessKey, document.Protocol, document.CancelReason, fiscalSettings);

            document.CancelCstat = result.Cstat;
            document.CancelXMotivo = result.XMotivo;
            document.UpdatedAt = DateTime.UtcNow;

            // 135 (evento registrado) and 155 (registrado fora de prazo) are the only homologations.
            if (result.Status == NFeStatus.Cancelled)
            {
                document.Status = NFeStatus.Cancelled;
                document.CancelProtocol = result.Protocol;
                document.CancelledAtUtc = result.IssuedAt ?? DateTime.UtcNow;
            }
            else
            {
                Log.Logger.Warning(
                    "NFe cancellation refused for document {DocumentId}: cStat {Cstat}.",
                    document.Id,
                    result.Cstat);
            }

            _nfeRepository.Update(document);
            await _nfeRepository.CommitAsync();

            return document;
        }

        /// <summary>Copies a SEFAZ verdict onto the document. One place, so consult and the reconciler agree.</summary>
        /// <remarks>
        /// Internal rather than private because <see cref="NFeReconciliationService"/> adopts a
        /// verdict through exactly the same rules; two copies of this drifted once already.
        /// </remarks>
        internal static void ApplyOutcome(NFeDocument document, EvangelionERPV2.NFeModule.Infra.Providers.NFeProviderResult result)
        {
            document.Status = result.Status;
            document.Cstat = result.Cstat;
            document.XMotivo = result.XMotivo;

            if (!string.IsNullOrWhiteSpace(result.Protocol))
                document.Protocol = result.Protocol;

            if (!string.IsNullOrWhiteSpace(result.XmlContent))
                document.XmlContent = result.XmlContent;

            // NFCe only, and only ever set — never cleared here. A note that authorised once carries
            // a QR the customer may check for years; a later consult that answers without one must
            // not erase it.
            if (!string.IsNullOrWhiteSpace(result.QrCode))
                document.QrCode = result.QrCode;

            if (!string.IsNullOrWhiteSpace(result.UrlChave))
                document.UrlChave = result.UrlChave;

            if (result.IssuedAt.HasValue)
            {
                document.IssuedAt = result.IssuedAt;

                if (result.Status == NFeStatus.Authorized)
                    document.AuthorizedAtUtc = result.IssuedAt;
            }

            // SEFAZ answering "this chave does not exist" (cStat 217) is the only evidence strong
            // enough to retract the write-ahead marker. Clearing it is what lets the allocated
            // number be sent again instead of being burned: CanReuseAllocation reads exactly this
            // column, so leaving it set would quietly re-allocate and leak the number to
            // inutilização paperwork nobody is going to file.
            if (result.NeverTransmitted)
                document.TransmittedAtUtc = null;

            document.UpdatedAt = DateTime.UtcNow;
        }

        /// <summary>
        /// Steps 2-4: allocate the nNF, build the chave from it, and commit both while the document
        /// is still provably untransmitted.
        /// </summary>
        /// <remarks>
        /// The chave has to exist in the database <em>before</em> the first byte leaves, because it
        /// is the only handle a reconciler has on a note whose outcome is unknown. Everything that
        /// can fail without consuming a number is done before the allocation — a burnt number is
        /// legal but permanent, and the remedy (inutilização) is out of scope for v1.
        /// </remarks>
        private async Task AssignNumberAndAccessKeyAsync(
            NFeDocument document,
            EnterpriseFiscalSettings fiscalSettings,
            NFeDocumentType type,
            int tipoEmissao = NormalEmission)
        {
            var emission = EmitterNow();

            // A retry of a document that was claimed but provably never transmitted keeps the number
            // it already allocated. Skipping it instead would need inutilização paperwork for a
            // number nobody ever used — see NFeReconciliationService.
            if (CanReuseAllocation(document, emission, tipoEmissao))
            {
                Log.Logger.Information(
                    "NFe document {DocumentId} reuses its allocated number {Number}; it was never transmitted.",
                    document.Id,
                    document.Number);
                return;
            }

            var enterpriseId = fiscalSettings.EnterpriseId;

            // GetSettingsAsync returns a transient default for a tenant that never configured
            // anything, so an unconfigured tenant would otherwise reach NFeAccessKeyBuilder with an
            // empty CNPJ and a cUF of 0. Rejected here rather than there, to keep the message about
            // the tenant instead of about an argument.
            if (string.IsNullOrWhiteSpace(fiscalSettings.Cnpj) || fiscalSettings.CodigoUfIbge <= 0)
                throw new InsertDatabaseException("Fiscal settings are not configured for this enterprise.");

            var model = NFeDocument.ModelFor(type);

            // Contingency has its own series so its numbering cannot collide with the online one.
            var series = type switch
            {
                NFeDocumentType.NFCe when tipoEmissao == OfflineNFCeEmission => fiscalSettings.NFCeContingencySeries,
                NFeDocumentType.NFCe => fiscalSettings.NFCeSeries,
                _ => fiscalSettings.NFeSeries
            };
            if (!int.TryParse(series, NumberStyles.None, CultureInfo.InvariantCulture, out var seriesNumber))
                throw new InsertDatabaseException($"Fiscal series '{series}' is not numeric.");

            var number = await _nfeRepositoryCustom.AllocateNextNumberAsync(
                enterpriseId,
                model,
                series,
                fiscalSettings.Environment)
                ?? throw new InsertDatabaseException("NFe number sequence is not configured for this enterprise.");

            document.Number = number.ToString(CultureInfo.InvariantCulture);
            document.Series = series;
            document.Environment = EnvironmentName(fiscalSettings.Environment);

            // cNF is not persisted separately: it is digits 36-43 of the chave, so the key itself is
            // enough to rebuild or verify the document.
            document.AccessKey = NFeAccessKeyBuilder.Build(
                fiscalSettings.CodigoUfIbge,
                emission,
                fiscalSettings.Cnpj,
                model,
                seriesNumber,
                number,
                tipoEmissao,
                NFeAccessKeyBuilder.GenerateCodigoNumerico(number));

            document.TipoEmissao = (byte)tipoEmissao;

            document.UpdatedAt = DateTime.UtcNow;

            _nfeRepository.Update(document);
            await _nfeRepository.CommitAsync();
        }

        /// <summary>
        /// True when the document already carries an allocation that is still safe to transmit.
        /// </summary>
        /// <remarks>
        /// Four conditions, all load-bearing: the write-ahead marker must be NULL (otherwise the
        /// note may already exist at SEFAZ and reusing the key double-emits), the chave must still
        /// describe this document's number, its AAMM must be the current month — an old chave
        /// re-transmitted in a new month is rejected — and its tpEmis must be the one being issued
        /// now. Anything else falls through to a fresh allocation, which burns a number but is
        /// never wrong.
        ///
        /// The tpEmis condition is what makes the till's contingency fallback work at all: the
        /// retry that follows a failed online attempt asks for tpEmis 9, and reusing the online
        /// allocation would keep the online series and a chave that says tpEmis 1 while the XML
        /// says 9 — a note that contradicts the key printed on the customer's receipt.
        /// </remarks>
        private static bool CanReuseAllocation(NFeDocument document, DateTime emission, int tipoEmissao)
        {
            if (document.TransmittedAtUtc != null || string.IsNullOrWhiteSpace(document.Number))
                return false;

            if (!NFeAccessKeyBuilder.IsValid(document.AccessKey))
                return false;

            var sameMonth = document.AccessKey.Substring(2, 4)
                == emission.ToString("yyMM", CultureInfo.InvariantCulture);
            var sameNumber = document.AccessKey.Substring(25, 9) == document.Number.PadLeft(9, '0');

            // Digit 35 of the chave (index 34) is tpEmis.
            var sameEmissionType = document.AccessKey[34] == (char)('0' + tipoEmissao);

            return sameMonth && sameNumber && sameEmissionType;
        }

        /// <summary>
        /// Step 5 — the write-ahead marker, committed on its own immediately before transmission.
        /// </summary>
        /// <remarks>
        /// A separate commit on purpose. The fiscal library is fully synchronous and a cancelled or
        /// timed-out call cannot recall bytes already on the wire, so this has to be durable before
        /// the call rather than alongside it. It is what makes <see cref="NFeDocument.IsOutcomeUnknown"/>
        /// mean something.
        /// </remarks>
        private async Task MarkTransmittedAsync(NFeDocument document)
        {
            var now = DateTime.UtcNow;
            document.TransmittedAtUtc = now;
            document.UpdatedAt = now;

            _nfeRepository.Update(document);
            await _nfeRepository.CommitAsync();
        }

        /// <summary>
        /// Now in the emitter's timezone, which is what AAMM in the chave must match — a UTC
        /// instant crosses the month boundary three hours early in Brazil.
        /// </summary>
        private static DateTime EmitterNow()
        {
            // ponytail: one national offset rather than a per-tenant timezone. Brazil has had no DST
            // since 2019 and the v1 UF allowlist (SP/PR/SC/RS) is entirely UTC-3, so the tz database
            // is the authority and the fixed offset only covers an image shipped without tzdata.
            // Becomes wrong if the allowlist grows to AM/AC/MS/MT — take the UF's zone then.
            try
            {
                return TimeZoneInfo.ConvertTimeFromUtc(
                    DateTime.UtcNow,
                    TimeZoneInfo.FindSystemTimeZoneById("America/Sao_Paulo"));
            }
            catch (Exception exception) when (exception is TimeZoneNotFoundException or InvalidTimeZoneException)
            {
                return DateTime.UtcNow.AddHours(-3);
            }
        }

        /// <summary>tpAmb as stored on the document. 1 = Produção, anything else = Homologação.</summary>
        private static string EnvironmentName(byte environment) =>
            environment == ProductionEnvironment ? "Production" : "Homologation";

        private async Task<Order?> GetScopedOrderAsync(Guid orderId, Guid enterpriseId)
        {
            if (enterpriseId == Guid.Empty)
                return null;

            var order = await _orderRepository.GetByIdAsync(orderId);
            if (order == null)
                return null;

            if (order.EnterpriseId != enterpriseId)
                return null;

            return order;
        }

        private async Task<bool> IsOrderWithinEnterpriseAsync(Guid orderId, Guid enterpriseId)
        {
            if (enterpriseId == Guid.Empty)
                return false;

            var order = await _orderRepository.GetByIdAsync(orderId);
            return order != null && order.EnterpriseId == enterpriseId;
        }

        /// <summary>
        /// The single access-key read path for <c>Consult</c> and <c>Cancel</c>, tenant-scoped in SQL.
        /// </summary>
        /// <remarks>
        /// Scoping in the query rather than in the caller is the point: this is the only defence
        /// against reading another tenant's fiscal document by access key, and a caller that forgets
        /// the check is an IDOR. The common path never materialises a foreign row at all. The
        /// unscoped fallback exists only for rows predating the denormalised <c>EnterpriseId</c>
        /// column (backfilled by <c>20260801130000</c>), and it still verifies ownership through the
        /// Order before returning anything.
        /// </remarks>
        private async Task<NFeDocument?> GetScopedDocumentByAccessKeyAsync(string accessKey, Guid enterpriseId)
        {
            if (string.IsNullOrWhiteSpace(accessKey) || enterpriseId == Guid.Empty)
                return null;

            var scoped = await _nfeRepositoryCustom.GetByAccessKeyAsync(accessKey, enterpriseId);
            if (scoped != null)
                return scoped;

            var legacy = await _nfeRepositoryCustom.GetByAccessKeyAsync(accessKey);
            if (legacy == null || legacy.EnterpriseId != Guid.Empty)
                return null;

            var order = await _orderRepository.GetByIdAsync(legacy.OrderId);
            return order != null && order.EnterpriseId == enterpriseId ? legacy : null;
        }

        /// <summary>
        /// Returns an Error document to a clean claimed state so it can be issued again.
        /// </summary>
        /// <remarks>
        /// Only ever valid for <see cref="NFeStatus.Error"/> — our own failure, before any
        /// certainty of transmission. Resetting a document whose outcome is unknown would clear
        /// the access key and the write-ahead marker that make duplicate emission detectable,
        /// so the guard below is a correctness control, not defensive style.
        /// </remarks>
        private static NFeDocument ResetErroredDocumentForRetry(NFeDocument document)
        {
            if (document.Status != NFeStatus.Error)
                throw new InvalidOperationException("Only documents in Error state may be reset for retry.");

            // An Error document was never transmitted — a failure after transmission stays Pending
            // with an unknown outcome — so its allocation is still unused and is kept for reuse.
            // AssignNumberAndAccessKeyAsync decides whether it is still valid; clearing it here
            // would burn the number unconditionally.
            var neverTransmitted = document.TransmittedAtUtc == null;

            document.Status = NFeStatus.Pending;
            if (!neverTransmitted)
            {
                document.AccessKey = string.Empty;
                document.Series = string.Empty;
                document.Number = string.Empty;
                document.Environment = string.Empty;
            }

            document.Protocol = string.Empty;
            document.IssuedAt = null;
            document.XmlContent = string.Empty;

            document.Cstat = string.Empty;
            document.XMotivo = string.Empty;
            document.ReceiptNumber = string.Empty;
            document.TransmittedAtUtc = null;
            document.AuthorizedAtUtc = null;
            document.DigestValue = string.Empty;
            document.QrCode = string.Empty;
            document.UrlChave = string.Empty;

            document.UpdatedAt = DateTime.UtcNow;
            document.IsActive = true;
            return document;
        }

        #region Dispose Pattern
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!disposed)
            {
                if (disposing)
                {
                    (_nfeRepository as IDisposable)?.Dispose();
                    (_nfeRepositoryCustom as IDisposable)?.Dispose();
                    (_orderRepository as IDisposable)?.Dispose();
                    (_customerRepository as IDisposable)?.Dispose();
                    (_enterpriseRepository as IDisposable)?.Dispose();
                }

                disposed = true;
            }
        }

        ~NFeService()
        {
            Dispose(false);
        }
        #endregion

        private static SemaphoreSlim[] CreateIssuanceLocks()
        {
            var locks = new SemaphoreSlim[IssuanceLockPoolSize];
            for (var i = 0; i < IssuanceLockPoolSize; i++)
            {
                locks[i] = new SemaphoreSlim(1, 1);
            }

            return locks;
        }

        private static SemaphoreSlim GetIssuanceLock(Guid orderId, NFeDocumentType type)
        {
            var hash = HashCode.Combine(orderId, type);
            var index = (hash & int.MaxValue) % IssuanceLockPoolSize;
            return IssuanceLocks[index];
        }

        private async Task<NFeDocument?> WaitForFinalizedIssuanceAsync(Guid orderId, NFeDocumentType type, NFeDocument? currentDocument = null)
        {
            var deadline = DateTime.UtcNow.Add(IssuanceClaimPollTimeout);
            var document = currentDocument;

            while (DateTime.UtcNow < deadline)
            {
                document = await _nfeRepositoryCustom.GetByOrderIdAsync(orderId, type);
                if (document == null || document.Status != NFeStatus.Pending)
                    return document;

                await Task.Delay(IssuanceClaimPollDelay);
            }

            throw new InsertDatabaseException("NFe issuance is still in progress. Please retry shortly.");
        }
    }
}
