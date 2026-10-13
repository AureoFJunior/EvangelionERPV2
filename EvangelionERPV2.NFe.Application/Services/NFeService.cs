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
    public class NFeService : INFeService<NFeDocument>
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

        private readonly EvangelionERPV2.Shared.Repositories.IRepository<PdvSale> _pdvSaleRepository;
        private readonly EvangelionERPV2.Shared.Repositories.IRepository<PdvSalePayment> _pdvPaymentRepository;


        public NFeService(
            EvangelionERPV2.Shared.Repositories.IRepository<NFeDocument> nfeRepository,
            INFeRepository<NFeDocument> nfeRepositoryCustom,
            EvangelionERPV2.Shared.Repositories.IRepository<Order> orderRepository,
            EvangelionERPV2.Shared.Repositories.IRepository<Customer> customerRepository,
            EvangelionERPV2.Shared.Repositories.IRepository<Enterprise> enterpriseRepository,
            INFeProvider provider,
            INFeFiscalSettingsService fiscalSettingsService,
            IOptions<NFeSettings> settings,
            EvangelionERPV2.Shared.Repositories.IRepository<PdvSale> pdvSaleRepository,
            EvangelionERPV2.Shared.Repositories.IRepository<PdvSalePayment> pdvPaymentRepository)
        {
            _pdvSaleRepository = pdvSaleRepository;
            _pdvPaymentRepository = pdvPaymentRepository;
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
                    var tipoEmissao = contingency is null ? NormalEmission : OfflineNFCeEmission;

                    if (contingency is not null && type != NFeDocumentType.NFCe)
                        throw new InsertDatabaseException("Off-line contingency (tpEmis 9) exists only for NFC-e.");

                    await AssignNumberAndAccessKeyAsync(pendingDocument, fiscalSettings, type, tipoEmissao);

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
                        Payments = await GetTillTendersAsync(orderId, type),
                        AccessKey = pendingDocument.AccessKey,
                        Number = pendingDocument.Number,
                        Series = pendingDocument.Series,
                        TipoEmissao = pendingDocument.TipoEmissao,
                        ContingencyEnteredAt = pendingDocument.ContingencyEnteredAt,
                        ContingencyJustification = pendingDocument.ContingencyJustification
                    });

                    ApplyOutcome(pendingDocument, result);

                    pendingDocument.ReceiptNumber = result.ReceiptNumber;
                    pendingDocument.TotalValue = result.TotalValue;

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

            var authorizedAt = document.AuthorizedAtUtc ?? document.IssuedAt;
            if (authorizedAt.HasValue && DateTime.UtcNow > authorizedAt.Value.AddMinutes(windowMinutes))
                throw new InsertDatabaseException("The cancellation window for this document has closed.");

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

        /// <summary>
        /// How the counter was paid, for the note's <c>pag</c> group. Null for anything that is not
        /// a till sale.
        /// </summary>
        private async Task<IReadOnlyList<PdvSalePayment>?> GetTillTendersAsync(Guid orderId, NFeDocumentType type)
        {
            if (type != NFeDocumentType.NFCe)
                return null;

            var sale = (await _pdvSaleRepository.GetAllAsync(x => x.OrderId == orderId)).FirstOrDefault();
            if (sale == null)
                return null;

            var tenders = (await _pdvPaymentRepository.GetAllAsync(x => x.PdvSaleId == sale.Id)).ToList();

            if (tenders.Count == 0)
            {
                Log.Logger.Warning(
                    "PDV sale {PdvSaleId} has no tender rows; its NFC-e will declare tPag 99.", sale.Id);

                return null;
            }

            return tenders;
        }

        /// <summary>Copies a SEFAZ verdict onto the document. One place, so consult and the reconciler agree.</summary>
        internal static void ApplyOutcome(NFeDocument document, EvangelionERPV2.NFeModule.Infra.Providers.NFeProviderResult result)
        {
            document.Status = result.Status;
            document.Cstat = result.Cstat;
            document.XMotivo = result.XMotivo;

            if (!string.IsNullOrWhiteSpace(result.Protocol))
                document.Protocol = result.Protocol;

            if (!string.IsNullOrWhiteSpace(result.XmlContent))
                document.XmlContent = result.XmlContent;

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

            if (result.NeverTransmitted)
                document.TransmittedAtUtc = null;

            document.UpdatedAt = DateTime.UtcNow;
        }

        /// <summary>
        /// Steps 2-4: allocate the nNF, build the chave from it, and commit both while the document
        /// is still provably untransmitted.
        /// </summary>
        private async Task AssignNumberAndAccessKeyAsync(
            NFeDocument document,
            EnterpriseFiscalSettings fiscalSettings,
            NFeDocumentType type,
            int tipoEmissao = NormalEmission)
        {
            var emission = EmitterNow();

            if (CanReuseAllocation(document, emission, tipoEmissao))
            {
                Log.Logger.Information(
                    "NFe document {DocumentId} reuses its allocated number {Number}; it was never transmitted.",
                    document.Id,
                    document.Number);
                return;
            }

            var enterpriseId = fiscalSettings.EnterpriseId;

            if (string.IsNullOrWhiteSpace(fiscalSettings.Cnpj) || fiscalSettings.CodigoUfIbge <= 0)
                throw new InsertDatabaseException("Fiscal settings are not configured for this enterprise.");

            var model = NFeDocument.ModelFor(type);

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
        private static bool CanReuseAllocation(NFeDocument document, DateTime emission, int tipoEmissao)
        {
            if (document.TransmittedAtUtc != null || string.IsNullOrWhiteSpace(document.Number))
                return false;

            if (!NFeAccessKeyBuilder.IsValid(document.AccessKey))
                return false;

            var sameMonth = document.AccessKey.Substring(2, 4)
                == emission.ToString("yyMM", CultureInfo.InvariantCulture);
            var sameNumber = document.AccessKey.Substring(25, 9) == document.Number.PadLeft(9, '0');

            var sameEmissionType = document.AccessKey[34] == (char)('0' + tipoEmissao);

            return sameMonth && sameNumber && sameEmissionType;
        }

        /// <summary>
        /// Step 5 — the write-ahead marker, committed on its own immediately before transmission.
        /// </summary>
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
        private static NFeDocument ResetErroredDocumentForRetry(NFeDocument document)
        {
            if (document.Status != NFeStatus.Error)
                throw new InvalidOperationException("Only documents in Error state may be reset for retry.");

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
