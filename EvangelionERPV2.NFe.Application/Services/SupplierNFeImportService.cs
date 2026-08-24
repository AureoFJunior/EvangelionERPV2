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
    /// <para>
    /// One payable per duplicata. A nota paid in three instalments is three payments the bank will
    /// see on three different days, and a single bill for the whole vNF would sit in the carteira
    /// against a cash flow that never happens. A nota with no <c>cobr</c> block is one bill for the
    /// total, thirty days out. The item lines ride the first instalment only — the goods arrive
    /// once, whatever the payment schedule says.
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
                .OrderBy(x => x.DueDate)
                .ToList();

            if (existing.Count > 0)
            {
                // The unique index would refuse the inserts anyway; answering with the bills that
                // already exist is what makes re-dropping the same file harmless.
                var supplierId = existing[0].SupplierId;
                var supplier = supplierId.HasValue
                    ? (await _supplierRepository.GetAllAsync(x => x.Id == supplierId.Value)).FirstOrDefault()
                    : null;

                return new SupplierNFeImportResultDTO
                {
                    PayableBillId = existing[0].Id,
                    PayableBillIds = existing.Select(x => x.Id).ToList(),
                    AccessKey = nota.AccessKey,
                    Number = nota.Number,
                    IssuedAt = nota.IssuedAt,
                    DueDate = existing[0].DueDate,
                    Amount = existing.Sum(x => x.Amount),
                    SupplierId = supplierId ?? Guid.Empty,
                    SupplierName = supplier?.Name ?? string.Empty,
                    AlreadyImported = true
                };
            }

            var (supplierRecord, supplierCreated) = await ResolveSupplierAsync(enterpriseId, nota);
            var (items, unmatched) = await MatchItemsAsync(enterpriseId, nota);

            var instalments = ResolveInstalments(nota);
            var created = new List<PayableBill>();

            for (var index = 0; index < instalments.Count; index++)
            {
                var instalment = instalments[index];

                var bill = new PayableBill
                {
                    EnterpriseId = enterpriseId,
                    SupplierId = supplierRecord.Id,
                    SupplierNFeAccessKey = nota.AccessKey,
                    SupplierNFeDuplicata = instalment.Number,
                    Description = BuildDescription(nota, supplierRecord.Name, index, instalments.Count),
                    DueDate = instalment.DueDate,
                    Amount = instalment.Amount,
                    IsPaid = false,
                    BillType = (int)(items.Count > 0 ? EnumPayableBillType.ProductStock : EnumPayableBillType.Other),
                    // Only the first instalment carries the goods: the delivery happens once, and
                    // repeating the lines would let a human receive the same stock three times.
                    Items = index == 0 && items.Count > 0 ? items : null
                };

                // keepProvidedAmount: the payable is what the nota says is owed — vDup per
                // instalment, or vNF for a nota with no schedule — and neither is the sum of the
                // line values, which exclude freight, insurance and ST.
                created.Add(await _payableBillService.CreateAsync(bill, keepProvidedAmount: true));
            }

            Log.Logger.Information(
                "Supplier NF-e imported. Instalments={Instalments} MatchedItems={MatchedItems} UnmatchedItems={UnmatchedItems}",
                created.Count, items.Count, unmatched.Count);

            return new SupplierNFeImportResultDTO
            {
                PayableBillId = created[0].Id,
                PayableBillIds = created.Select(x => x.Id).ToList(),
                AccessKey = nota.AccessKey,
                Number = nota.Number,
                IssuedAt = nota.IssuedAt,
                DueDate = created[0].DueDate,
                Amount = created.Sum(x => x.Amount),
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

        /// <summary>One payable's worth of the nota: its duplicata number, date and amount.</summary>
        private sealed record Instalment(string? Number, DateTime DueDate, decimal Amount);

        /// <summary>
        /// The nota's payment schedule, or a single instalment for the whole total when it has none.
        /// </summary>
        /// <remarks>
        /// A duplicata with no amount, or a schedule whose amounts are all zero, is treated as no
        /// schedule at all: some emitters send a <c>cobr</c> block with only a <c>fat</c> in it, and
        /// generating a payable for zero would put a bill nobody owes in the carteira.
        /// </remarks>
        private static List<Instalment> ResolveInstalments(SupplierNFeDocument nota)
        {
            var scheduled = nota.Duplicatas
                .Where(x => x.Amount > 0)
                .Select((duplicata, index) => new Instalment(
                    // The number is the key's second half, so it must exist. Emitters do omit it.
                    string.IsNullOrWhiteSpace(duplicata.Number) ? $"{index + 1:000}" : duplicata.Number.Trim(),
                    duplicata.DueDate?.Date ?? nota.IssuedAt.Date.AddDays(DefaultPaymentTermInDays),
                    duplicata.Amount))
                .ToList();

            if (scheduled.Count > 0)
                return scheduled;

            return [new Instalment(null, nota.IssuedAt.Date.AddDays(DefaultPaymentTermInDays), nota.TotalAmount)];
        }

        private static string BuildDescription(
            SupplierNFeDocument nota,
            string supplierName,
            int index,
            int total)
        {
            var head = $"NF-e {nota.Number} - {supplierName}";
            return total > 1 ? $"{head} ({index + 1}/{total})" : head;
        }
    }
}
