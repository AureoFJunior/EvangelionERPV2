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

        public async Task<NFeDocument?> GetByOrderIdAsync(Guid orderId, NFeDocumentType? type = null)
        {
            if (orderId == Guid.Empty)
                return null;

            return await _nfeRepositoryCustom.GetByOrderIdAsync(orderId, type);
        }

        public async Task<NFeDocument?> IssueAsync(Guid orderId, NFeDocumentType type)
        {
            if (!_settings.Enabled)
            {
                Log.Logger.Information("NFe/NFCe issuance skipped: disabled in settings.");
                return null;
            }

            if (orderId == Guid.Empty)
                throw new InsertDatabaseException("Order Id is invalid for NFe issuance.");

            var issuanceLock = GetIssuanceLock(orderId, type);
            await issuanceLock.WaitAsync();
            try
            {
                var existing = await _nfeRepositoryCustom.GetByOrderIdAsync(orderId, type);
                if (existing != null)
                    return existing;

                var order = await _orderRepository.GetByIdAsync(orderId);
                if (order == null)
                    throw new NotFoundDatabaseException($"{nameof(Order)} was not found.");

                var enterprise = order.EnterpriseId.HasValue ? await _enterpriseRepository.GetByIdAsync(order.EnterpriseId.Value) : null;
                var customer = order.CustomerId.HasValue ? await _customerRepository.GetByIdAsync(order.CustomerId.Value) : null;

                var result = await _provider.IssueAsync(order, enterprise, customer, type, _settings);

                var document = new NFeDocument
                {
                    Id = Guid.NewGuid(),
                    OrderId = order.Id,
                    Type = type,
                    Status = result.Status,
                    AccessKey = result.AccessKey,
                    Series = result.Series,
                    Number = result.Number,
                    Environment = result.Environment,
                    Protocol = result.Protocol,
                    IssuedAt = result.IssuedAt ?? DateTime.UtcNow,
                    TotalValue = result.TotalValue,
                    XmlContent = result.XmlContent
                };

                await _nfeRepository.CreateAsync(document);
                try
                {
                    await _nfeRepository.CommitAsync();
                }
                catch (DbUpdateException commitException)
                {
                    var concurrentDocument = await _nfeRepositoryCustom.GetByOrderIdAsync(orderId, type);
                    if (concurrentDocument == null)
                        throw;

                    Log.Logger.Warning(
                        commitException,
                        "NFe issuance duplicate detected for order {OrderId} and type {Type}. Returning existing document {DocumentId}.",
                        orderId,
                        type,
                        concurrentDocument.Id);

                    return concurrentDocument;
                }

                return document;
            }
            finally
            {
                issuanceLock.Release();
            }
        }

        public async Task<NFeDocument?> ConsultAsync(string accessKey)
        {
            if (string.IsNullOrWhiteSpace(accessKey))
                return null;

            return await _nfeRepositoryCustom.GetByAccessKeyAsync(accessKey);
        }

        public async Task<NFeDocument?> CancelAsync(string accessKey, string reason)
        {
            if (string.IsNullOrWhiteSpace(accessKey))
                return null;

            var document = await _nfeRepositoryCustom.GetByAccessKeyAsync(accessKey);
            if (document == null)
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
    }
}
