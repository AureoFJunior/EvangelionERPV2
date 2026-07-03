using EvangelionERPV2.NFeModule.Application.Configs;
using EvangelionERPV2.NFeModule.Application.Interface;
using EvangelionERPV2.NFeModule.Application.Providers;
using EvangelionERPV2.NFeModule.Domain.Interface;
using EvangelionERPV2.Shared.Entities;
using EvangelionERPV2.Shared.Exceptions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Serilog;

namespace EvangelionERPV2.NFeModule.Application.Services
{
    public class NFeService : INFeService<NFeDocument>, IDisposable
    {
        private const int IssuanceLockPoolSize = 256;
        private static readonly TimeSpan IssuanceClaimPollDelay = TimeSpan.FromMilliseconds(200);
        private static readonly TimeSpan IssuanceClaimPollTimeout = TimeSpan.FromSeconds(15);
        private static readonly SemaphoreSlim[] IssuanceLocks = CreateIssuanceLocks();

        private readonly EvangelionERPV2.Shared.Repositories.IRepository<NFeDocument> _nfeRepository;
        private readonly INFeRepository<NFeDocument> _nfeRepositoryCustom;
        private readonly EvangelionERPV2.Shared.Repositories.IRepository<Order> _orderRepository;
        private readonly EvangelionERPV2.Shared.Repositories.IRepository<Customer> _customerRepository;
        private readonly EvangelionERPV2.Shared.Repositories.IRepository<Enterprise> _enterpriseRepository;
        private readonly INFeProvider _provider;
        private readonly NFeSettings _settings;
        private bool disposed;

        public NFeService(
            EvangelionERPV2.Shared.Repositories.IRepository<NFeDocument> nfeRepository,
            INFeRepository<NFeDocument> nfeRepositoryCustom,
            EvangelionERPV2.Shared.Repositories.IRepository<Order> orderRepository,
            EvangelionERPV2.Shared.Repositories.IRepository<Customer> customerRepository,
            EvangelionERPV2.Shared.Repositories.IRepository<Enterprise> enterpriseRepository,
            INFeProvider provider,
            IOptions<NFeSettings> settings)
        {
            _nfeRepository = nfeRepository;
            _nfeRepositoryCustom = nfeRepositoryCustom;
            _orderRepository = orderRepository;
            _customerRepository = customerRepository;
            _enterpriseRepository = enterpriseRepository;
            _provider = provider;
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

        public async Task<NFeDocument?> IssueAsync(Guid orderId, Guid enterpriseId, NFeDocumentType type = NFeDocumentType.NFe)
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
                        Type = type,
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
                    var result = await _provider.IssueAsync(order, enterprise, customer, type, _settings);

                    pendingDocument.Status = result.Status;
                    pendingDocument.AccessKey = result.AccessKey;
                    pendingDocument.Series = result.Series;
                    pendingDocument.Number = result.Number;
                    pendingDocument.Environment = result.Environment;
                    pendingDocument.Protocol = result.Protocol;
                    pendingDocument.IssuedAt = result.IssuedAt ?? DateTime.UtcNow;
                    pendingDocument.TotalValue = result.TotalValue;
                    pendingDocument.XmlContent = result.XmlContent;
                    pendingDocument.UpdatedAt = DateTime.UtcNow;

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

                    throw;
                }
            }
            finally
            {
                issuanceLock.Release();
            }
        }

        public async Task<NFeDocument?> ConsultAsync(string accessKey, Guid enterpriseId)
        {
            if (string.IsNullOrWhiteSpace(accessKey))
                return null;

            var document = await _nfeRepositoryCustom.GetByAccessKeyAsync(accessKey);
            if (document == null)
                return null;

            if (!await IsDocumentWithinEnterpriseAsync(document, enterpriseId))
                return null;

            return document;
        }

        public async Task<NFeDocument?> CancelAsync(string accessKey, Guid enterpriseId, string reason = "")
        {
            if (string.IsNullOrWhiteSpace(accessKey))
                return null;

            var document = await _nfeRepositoryCustom.GetByAccessKeyAsync(accessKey);
            if (document == null)
                return null;

            if (!await IsDocumentWithinEnterpriseAsync(document, enterpriseId))
                return null;

            if (document.Status == NFeStatus.Cancelled)
                return document;

            document.Status = NFeStatus.Cancelled;
            document.CancelReason = reason ?? string.Empty;
            document.CancelProtocol = $"CANCEL-{DateTime.UtcNow:yyyyMMddHHmmss}";
            document.UpdatedAt = DateTime.UtcNow;

            _nfeRepository.Update(document);
            await _nfeRepository.CommitAsync();

            return document;
        }

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

        private async Task<bool> IsDocumentWithinEnterpriseAsync(NFeDocument document, Guid enterpriseId)
        {
            if (enterpriseId == Guid.Empty)
                return false;

            var order = await _orderRepository.GetByIdAsync(document.OrderId);
            return order != null && order.EnterpriseId == enterpriseId;
        }

        private static NFeDocument ResetErroredDocumentForRetry(NFeDocument document)
        {
            document.Status = NFeStatus.Pending;
            document.AccessKey = string.Empty;
            document.Series = string.Empty;
            document.Number = string.Empty;
            document.Environment = string.Empty;
            document.Protocol = string.Empty;
            document.IssuedAt = null;
            document.XmlContent = string.Empty;
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
