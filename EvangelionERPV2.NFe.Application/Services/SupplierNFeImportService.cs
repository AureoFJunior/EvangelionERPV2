using EvangelionERPV2.BillsModule.Application.Interface;
using EvangelionERPV2.NFeModule.Application.Interface;
using EvangelionERPV2.NFeModule.Infra.Xml;
using EvangelionERPV2.Shared.DTOs;
using EvangelionERPV2.Shared.Entities;
using EvangelionERPV2.Shared.Enums;
using EvangelionERPV2.Shared.Repositories;
using Serilog;

namespace EvangelionERPV2.NFeModule.Application.Services
{
    /// <summary>
    /// A supplier's NF-e XML becomes a payable, and nothing else.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The XML <em>is</em> the document — there is no purchase order, no approval chain and no
    /// three-way match here, and this is not the beginning of a purchasing module. It replaces the
    /// highest-volume typing an SMB does, and it needs no certificate and no SEFAZ call, so it
    /// works today.
    /// </para>
    /// <para>
    /// Stock deliberately does not move at import. A nota is issued when goods <em>ship</em>;
    /// <c>MarkProductsReceived</c> is already the arrival event and, since M14, writes the ledger.
    /// Importing creates the obligation, a human confirms the arrival.
    /// </para>
    /// </remarks>
    public class SupplierNFeImportService : ISupplierNFeImportService
    {
        private const int DefaultPaymentTermInDays = 30;

        private readonly IRepository<PayableBill> _payableBillRepository;
        private readonly IRepository<Supplier> _supplierRepository;
        private readonly IRepository<Product> _productRepository;
        private readonly IPayableBillService _payableBillService;

        public SupplierNFeImportService(
            IRepository<PayableBill> payableBillRepository,
            IRepository<Supplier> supplierRepository,
            IRepository<Product> productRepository,
            IPayableBillService payableBillService)
        {
            _payableBillRepository = payableBillRepository;
            _supplierRepository = supplierRepository;
            _productRepository = productRepository;
            _payableBillService = payableBillService;
        }

        public async Task<SupplierNFeImportResultDTO> ImportAsync(
            Guid enterpriseId,
            byte[] xml,
            CancellationToken cancellation = default)
        {
            if (enterpriseId == Guid.Empty)
                throw new ArgumentException("Enterprise is required.", nameof(enterpriseId));

            var nota = SupplierNFeXmlReader.Read(xml);

            var existing = (await _payableBillRepository.GetAllAsync(
                    x => x.EnterpriseId == enterpriseId && x.SupplierNFeAccessKey == nota.AccessKey))
                .FirstOrDefault();

            if (existing != null)
            {
                // The unique index would refuse the insert anyway; answering with the bill that
                // already exists is what makes re-dropping the same file harmless.
                var supplier = existing.SupplierId.HasValue
                    ? (await _supplierRepository.GetAllAsync(x => x.Id == existing.SupplierId.Value)).FirstOrDefault()
                    : null;

                return new SupplierNFeImportResultDTO
                {
                    PayableBillId = existing.Id,
                    AccessKey = nota.AccessKey,
                    Number = nota.Number,
                    IssuedAt = nota.IssuedAt,
                    DueDate = existing.DueDate,
                    Amount = existing.Amount,
                    SupplierId = existing.SupplierId ?? Guid.Empty,
                    SupplierName = supplier?.Name ?? string.Empty,
                    AlreadyImported = true
                };
            }

            var (supplierRecord, supplierCreated) = await ResolveSupplierAsync(enterpriseId, nota);
            var (items, unmatched) = await MatchItemsAsync(enterpriseId, nota);

            var bill = new PayableBill
            {
                EnterpriseId = enterpriseId,
                SupplierId = supplierRecord.Id,
                SupplierNFeAccessKey = nota.AccessKey,
                Description = $"NF-e {nota.Number} - {supplierRecord.Name}",
                DueDate = ResolveDueDate(nota),
                Amount = nota.TotalAmount,
                IsPaid = false,
                BillType = (int)(items.Count > 0 ? EnumPayableBillType.ProductStock : EnumPayableBillType.Other),
                Items = items.Count > 0 ? items : null
            };

            // keepProvidedAmount: vNF is the payable, and it is not the sum of the lines — freight,
            // insurance and ST all live outside them.
            var created = await _payableBillService.CreateAsync(bill, keepProvidedAmount: true);

            Log.Logger.Information(
                "Supplier NF-e imported. PayableBillId={PayableBillId} MatchedItems={MatchedItems} UnmatchedItems={UnmatchedItems}",
                created.Id, items.Count, unmatched.Count);

            return new SupplierNFeImportResultDTO
            {
                PayableBillId = created.Id,
                AccessKey = nota.AccessKey,
                Number = nota.Number,
                IssuedAt = nota.IssuedAt,
                DueDate = created.DueDate,
                Amount = created.Amount,
                SupplierId = supplierRecord.Id,
                SupplierName = supplierRecord.Name,
                SupplierCreated = supplierCreated,
                MatchedItemCount = items.Count,
                UnmatchedItems = unmatched
            };
        }

        /// <summary>
        /// The emitter, matched on its document. Unknown emitters become suppliers — unlike a
        /// product, a supplier record carries nothing the nota does not already state.
        /// </summary>
        private async Task<(Supplier Supplier, bool Created)> ResolveSupplierAsync(Guid enterpriseId, SupplierNFeDocument nota)
        {
            var existing = (await _supplierRepository.GetAllAsync(
                    x => x.EnterpriseId == enterpriseId && x.Document == nota.EmitterDocument))
                .FirstOrDefault();

            if (existing != null)
                return (existing, false);

            var supplier = new Supplier
            {
                Id = Guid.NewGuid(),
                EnterpriseId = enterpriseId,
                Name = nota.EmitterName,
                Document = nota.EmitterDocument,
                Adress = nota.EmitterAddress,
                PhoneNumber = string.Empty,
                Email = string.Empty,
                IsActive = true
            };

            await _supplierRepository.CreateAsync(supplier);
            await _supplierRepository.CommitAsync();

            return (supplier, true);
        }

        /// <summary>
        /// Nota lines against the inventory: barcode first, then the exact description.
        /// </summary>
        /// <remarks>
        /// Never auto-creates a product. A line nobody can map is reported so a human maps it once
        /// and every future nota from that supplier lands whole.
        /// </remarks>
        private async Task<(List<PayableBillProduct> Items, List<SupplierNFeUnmatchedItemDTO> Unmatched)>
            MatchItemsAsync(Guid enterpriseId, SupplierNFeDocument nota)
        {
            var unmatched = new List<SupplierNFeUnmatchedItemDTO>();
            if (nota.Items.Count == 0)
                return ([], unmatched);

            var products = (await _productRepository.GetPagedAsync(
                    x => x.EnterpriseId == enterpriseId && x.IsActive == true))
                .Items;

            var byEan = products
                .Where(x => !string.IsNullOrWhiteSpace(x.Cean))
                .GroupBy(x => x.Cean!.Trim())
                .ToDictionary(x => x.Key, x => x.First());

            var byName = products
                .GroupBy(x => x.Name.Trim(), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(x => x.Key, x => x.First(), StringComparer.OrdinalIgnoreCase);

            // Keyed by product: two nota lines can carry the same product, and the payable's item
            // index is unique per (bill, product) — so they are merged rather than refused.
            var itemsByProduct = new Dictionary<Guid, PayableBillProduct>();

            foreach (var line in nota.Items)
            {
                var product = ResolveProduct(line, byEan, byName);
                if (product == null)
                {
                    unmatched.Add(new SupplierNFeUnmatchedItemDTO
                    {
                        Code = line.Code,
                        Ean = line.Ean,
                        Description = line.Description,
                        Quantity = line.Quantity,
                        UnitValue = line.UnitValue,
                        UnitOfMeasure = line.UnitOfMeasure
                    });

                    continue;
                }

                if (itemsByProduct.TryGetValue(product.Id, out var merged))
                {
                    merged.Quantity += line.Quantity;
                    continue;
                }

                itemsByProduct[product.Id] = new PayableBillProduct
                {
                    ProductId = product.Id,
                    Quantity = line.Quantity,
                    UnitValue = line.UnitValue
                };
            }

            return (itemsByProduct.Values.ToList(), unmatched);
        }

        private static Product? ResolveProduct(
            SupplierNFeItem line,
            Dictionary<string, Product> byEan,
            Dictionary<string, Product> byName)
        {
            if (!string.IsNullOrWhiteSpace(line.Ean) && byEan.TryGetValue(line.Ean!, out var byBarcode))
                return byBarcode;

            return byName.TryGetValue(line.Description.Trim(), out var byDescription) ? byDescription : null;
        }

        /// <summary>
        /// The first duplicata's vencimento, or the issue date plus thirty days when the nota
        /// carries no schedule at all.
        /// </summary>
        /// <remarks>
        /// ponytail: only the first duplicata is honoured. A nota paid in three instalments wants
        /// three bills, which collides with the access key being the idempotency — the fix is a
        /// widened key of {chNFe}-{nDup}, and it can wait for a supplier who actually instals.
        /// </remarks>
        private static DateTime ResolveDueDate(SupplierNFeDocument nota)
        {
            var firstScheduled = nota.Duplicatas.FirstOrDefault(x => x.DueDate.HasValue)?.DueDate;
            return firstScheduled ?? nota.IssuedAt.Date.AddDays(DefaultPaymentTermInDays);
        }
    }
}
