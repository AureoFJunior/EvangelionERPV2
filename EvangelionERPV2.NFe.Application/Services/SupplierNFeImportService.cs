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

            var instalments = ResolveInstalments(nota);

            var importedWholeBeforeInstalments = existing.Any(x => x.SupplierNFeDuplicata == null)
                && instalments.Count > 1;

            var missing = importedWholeBeforeInstalments
                ? []
                : instalments
                    .Where(instalment => !existing.Any(bill => bill.SupplierNFeDuplicata == instalment.Number))
                    .ToList();

            if (missing.Count == 0)
            {
                var supplierId = existing[0].SupplierId;
                var supplier = supplierId.HasValue
                    ? (await _supplierRepository.GetPagedAsync(x => x.Id == supplierId.Value)).Items.FirstOrDefault()
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

            var (items, unmatched, costed) = await MatchItemsAsync(enterpriseId, nota);
            await PersistBackfilledCostsAsync(costed);

            var (supplierRecord, supplierCreated) = await ResolveSupplierAsync(enterpriseId, nota);

            var created = new List<PayableBill>(existing);
            var supplierPending = supplierCreated;

            foreach (var instalment in missing)
            {
                var index = instalments.IndexOf(instalment);

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
                    Items = index == 0 && items.Count > 0 ? items : null
                };

                if (supplierPending)
                {
                    await _supplierRepository.ExecuteInTransactionAsync(async () =>
                    {
                        await _supplierRepository.CreateAsync(supplierRecord);
                        await _supplierRepository.CommitAsync();

                        created.Add(await _payableBillService.CreateAsync(
                            bill, useAmbientTransaction: true, keepProvidedAmount: true));
                    });

                    supplierPending = false;
                    continue;
                }

                created.Add(await _payableBillService.CreateAsync(bill, keepProvidedAmount: true));
            }

            created = created.OrderBy(x => x.DueDate).ToList();

            Log.Logger.Information(
                "Supplier NF-e imported. Instalments={Instalments} MatchedItems={MatchedItems} UnmatchedItems={UnmatchedItems} CostedProducts={CostedProducts}",
                created.Count, items.Count, unmatched.Count, costed.Count);

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
                CostedProductCount = costed.Count,
                UnmatchedItems = unmatched
            };
        }

        /// <summary>
        /// The emitter, matched on its document. Unknown emitters become suppliers — unlike a
        /// product, a supplier record carries nothing the nota does not already state.
        /// </summary>
        /// <returns>
        /// The supplier, and whether it is **new and not yet persisted**. A new one is deliberately
        /// left unsaved: the caller writes it in the same transaction as the first bill, so a
        /// refused import cannot leave a supplier the shop never traded with.
        /// </returns>
        private async Task<(Supplier Supplier, bool Created)> ResolveSupplierAsync(Guid enterpriseId, SupplierNFeDocument nota)
        {
            var existing = (await _supplierRepository.GetPagedAsync(
                    x => x.EnterpriseId == enterpriseId && x.Document == nota.EmitterDocument))
                .Items
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

            return (supplier, true);
        }

        /// <summary>
        /// Nota lines against the inventory: barcode first, then the exact description.
        /// </summary>
        private async Task<(List<PayableBillProduct> Items, List<SupplierNFeUnmatchedItemDTO> Unmatched, List<Product> Costed)>
            MatchItemsAsync(Guid enterpriseId, SupplierNFeDocument nota)
        {
            var unmatched = new List<SupplierNFeUnmatchedItemDTO>();
            var costed = new List<Product>();
            if (nota.Items.Count == 0)
                return ([], unmatched, costed);

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

                if (TryBackfillCost(product, line))
                    costed.Add(product);

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

            return (itemsByProduct.Values.ToList(), unmatched, costed);
        }

        /// <summary>
        /// Gives an uncosted product the unit value the supplier just charged for it.
        /// </summary>
        private static bool TryBackfillCost(Product product, SupplierNFeItem line)
        {
            if (product.CostPrice.HasValue || line.UnitValue <= 0)
                return false;

            // ponytail: takes vUnCom at face value. The nota's commercial unit is not necessarily
            // the unit the product is sold in - a box of twelve costed as one - and the two
            // vocabularies do not line up (EnumUnitOfMeasure says "Box", a nota says "CX"). Map
            // line.UnitOfMeasure to product.UnitOfMeasure and divide if a pilot gets this wrong;
            // until then a visible, editable number beats a permanent "—".
            product.CostPrice = line.UnitValue;
            return true;
        }

        /// <summary>
        /// Saves the costs the match filled in.
        /// </summary>
        private async Task PersistBackfilledCostsAsync(List<Product> costed)
        {
            if (costed.Count == 0)
                return;

            try
            {
                foreach (var product in costed)
                    _productRepository.Update(product);

                await _productRepository.CommitAsync();
            }
            catch (Exception ex)
            {
                Log.Logger.Error(
                    "Couldn't save costs backfilled from a supplier NF-e. Products={Products} ErrorType={ErrorType}",
                    costed.Count,
                    ex.GetType().Name);
            }
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
        private static List<Instalment> ResolveInstalments(SupplierNFeDocument nota)
        {
            var scheduled = nota.Duplicatas
                .Where(x => x.Amount > 0)
                .Select((duplicata, index) => new Instalment(
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
