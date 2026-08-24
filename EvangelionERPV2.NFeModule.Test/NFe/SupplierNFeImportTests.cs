using System.Linq.Expressions;
using System.Text;
using EvangelionERPV2.BillsModule.Application.Interface;
using EvangelionERPV2.NFeModule.Application.Services;
using EvangelionERPV2.NFeModule.Infra.Xml;
using EvangelionERPV2.Shared.Entities;
using EvangelionERPV2.Shared.Enums;
using EvangelionERPV2.Shared.Repositories;
using Moq;

namespace EvangelionERPV2.NFeModule.Test
{
    /// <summary>
    /// Reading a supplier's nota and turning it into a payable.
    /// </summary>
    /// <remarks>
    /// Every case runs against a structurally complete <c>nfeProc</c> — namespace, protocol,
    /// duplicatas and all — because the input this feature exists for is a file another company's
    /// ERP produced, not one shaped to suit the parser.
    /// </remarks>
    public class SupplierNFeImportTests
    {
        private static readonly Guid EnterpriseId = Guid.NewGuid();
        private const string AccessKey = "35260812345678000199550010000000421000000428";
        private const string EmitterCnpj = "12345678000199";

        #region Reader

        [Fact]
        public void Read_ParsesTheIdentityTotalsAndLines()
        {
            var nota = SupplierNFeXmlReader.Read(BuildNota());

            Assert.Equal(AccessKey, nota.AccessKey);
            Assert.Equal("42", nota.Number);
            Assert.Equal("1", nota.Series);
            Assert.Equal(new DateTime(2026, 8, 10, 13, 0, 0, DateTimeKind.Utc), nota.IssuedAt);

            // vNF, not the sum of the lines: this nota carries 20.00 of freight.
            Assert.Equal(320m, nota.TotalAmount);
            Assert.Equal(EmitterCnpj, nota.EmitterDocument);
            Assert.Equal("Nerv Supplies LTDA", nota.EmitterName);
            Assert.Contains("Tokyo-3", nota.EmitterAddress);

            Assert.Equal(2, nota.Items.Count);
            Assert.Equal("7891234567895", nota.Items[0].Ean);
            Assert.Equal(3m, nota.Items[0].Quantity);
            Assert.Equal(60m, nota.Items[0].UnitValue);

            // "SEM GTIN" is the layout's literal for a line with no barcode.
            Assert.Null(nota.Items[1].Ean);

            var duplicata = Assert.Single(nota.Duplicatas);
            Assert.Equal(new DateTime(2026, 9, 9, 0, 0, 0, DateTimeKind.Utc), duplicata.DueDate);
            Assert.Equal(320m, duplicata.Amount);
        }

        [Fact]
        public void Read_TakesTheKeyFromTheIdAttribute_WhenTheNotaCarriesNoProtocol()
        {
            // What an ERP exports before transmission: the nota alone, no protocol around it.
            var full = Encoding.UTF8.GetString(BuildNota());
            var start = full.IndexOf("<infNFe", StringComparison.Ordinal);
            var end = full.IndexOf("</infNFe>", StringComparison.Ordinal) + "</infNFe>".Length;
            var bare = $"<NFe xmlns=\"http://www.portalfiscal.inf.br/nfe\">{full[start..end]}</NFe>";

            var nota = SupplierNFeXmlReader.Read(Encoding.UTF8.GetBytes(bare));

            Assert.Equal(AccessKey, nota.AccessKey);
        }

        [Theory]
        [InlineData("")]
        [InlineData("not xml at all")]
        [InlineData("<html><body>404</body></html>")]
        public void Read_RefusesAnythingThatIsNotANota(string content)
        {
            Assert.Throws<FormatException>(() => SupplierNFeXmlReader.Read(Encoding.UTF8.GetBytes(content)));
        }

        #endregion

        #region Import

        [Fact]
        public async Task Import_CreatesThePayableForTheNotaTotal_AndTheSupplierBehindIt()
        {
            var product = BuildProduct("Plugsuit", ean: "7891234567895");
            var (service, bills, suppliers) = BuildSut(products: [product]);

            var result = await service.ImportAsync(EnterpriseId, BuildNota());

            var bill = Assert.Single(bills);
            Assert.Equal(AccessKey, bill.SupplierNFeAccessKey);

            // The payable is vNF — freight and ST live outside the line values.
            Assert.Equal(320m, bill.Amount);
            Assert.Equal(new DateTime(2026, 9, 9, 0, 0, 0, DateTimeKind.Utc), bill.DueDate);
            Assert.Equal((int)EnumPayableBillType.ProductStock, bill.BillType);

            var supplier = Assert.Single(suppliers);
            Assert.Equal(EmitterCnpj, supplier.Document);
            Assert.True(result.SupplierCreated);
            Assert.Equal(1, result.MatchedItemCount);
            Assert.False(result.AlreadyImported);
        }

        [Fact]
        public async Task Import_OfAnInstalmentNota_CreatesOnePayablePerDuplicata()
        {
            // Three payments the bank will see on three days: one bill for the whole vNF would sit
            // in the carteira against a cash flow that never happens.
            var product = BuildProduct("Plugsuit", ean: "7891234567895");
            var (service, bills, _) = BuildSut(products: [product]);

            var result = await service.ImportAsync(EnterpriseId, BuildInstalmentNota());

            Assert.Equal(3, bills.Count);
            Assert.Equal(3, result.PayableBillIds.Count);

            Assert.Equal(["001", "002", "003"], bills.Select(x => x.SupplierNFeDuplicata ?? string.Empty).ToArray());
            Assert.Equal([120m, 100m, 100m], bills.Select(x => x.Amount).ToArray());
            Assert.Equal(
                [new DateTime(2026, 9, 9), new DateTime(2026, 10, 9), new DateTime(2026, 11, 9)],
                bills.Select(x => x.DueDate).ToArray());

            // The reported total is the nota, the first due date is the first instalment.
            Assert.Equal(320m, result.Amount);
            Assert.Equal(new DateTime(2026, 9, 9), result.DueDate);
            Assert.All(bills, bill => Assert.Equal(AccessKey, bill.SupplierNFeAccessKey));
            Assert.Contains("(1/3)", bills[0].Description);
        }

        [Fact]
        public async Task Import_PutsTheGoodsOnTheFirstInstalmentOnly()
        {
            // The delivery happens once whatever the schedule says; repeating the lines would let a
            // human receive the same stock three times.
            var product = BuildProduct("Plugsuit", ean: "7891234567895");
            var (service, bills, _) = BuildSut(products: [product]);

            await service.ImportAsync(EnterpriseId, BuildInstalmentNota());

            Assert.NotNull(bills[0].Items);
            Assert.All(bills.Skip(1), bill => Assert.Null(bill.Items));
        }

        [Fact]
        public async Task Import_OfAnInstalmentNotaTwice_CreatesNothingMore()
        {
            var (service, bills, _) = BuildSut();

            await service.ImportAsync(EnterpriseId, BuildInstalmentNota());
            var second = await service.ImportAsync(EnterpriseId, BuildInstalmentNota());

            Assert.Equal(3, bills.Count);
            Assert.True(second.AlreadyImported);
            Assert.Equal(3, second.PayableBillIds.Count);
            Assert.Equal(320m, second.Amount);
        }

        [Fact]
        public async Task Import_OfANotaWithNoSchedule_IsOneBillForTheTotal()
        {
            var withoutCobr = Encoding.UTF8.GetString(BuildNota());
            var start = withoutCobr.IndexOf("<cobr>", StringComparison.Ordinal);
            var end = withoutCobr.IndexOf("</cobr>", StringComparison.Ordinal) + "</cobr>".Length;

            var (service, bills, _) = BuildSut();

            await service.ImportAsync(EnterpriseId, Encoding.UTF8.GetBytes(withoutCobr.Remove(start, end - start)));

            var bill = Assert.Single(bills);
            Assert.Null(bill.SupplierNFeDuplicata);
            Assert.Equal(320m, bill.Amount);
            Assert.DoesNotContain("/", bill.Description);
        }

        [Fact]
        public async Task Import_IgnoresAScheduleThatCarriesNoAmounts()
        {
            // Some emitters send a cobr block with only a fat in it. A payable for zero is a bill
            // nobody owes.
            var emptySchedule = Encoding.UTF8.GetString(BuildNota())
                .Replace("<vDup>320.00</vDup>", "<vDup>0.00</vDup>");

            var (service, bills, _) = BuildSut();

            await service.ImportAsync(EnterpriseId, Encoding.UTF8.GetBytes(emptySchedule));

            var bill = Assert.Single(bills);
            Assert.Null(bill.SupplierNFeDuplicata);
            Assert.Equal(320m, bill.Amount);
        }

        [Fact]
        public async Task Import_NumbersADuplicataTheEmitterLeftBlank()
        {
            // The number is half the idempotency key, so it cannot be empty.
            var blankNumbers = Encoding.UTF8.GetString(BuildInstalmentNota())
                .Replace("<nDup>001</nDup>", "<nDup></nDup>")
                .Replace("<nDup>002</nDup>", "<nDup></nDup>")
                .Replace("<nDup>003</nDup>", "<nDup></nDup>");

            var (service, bills, _) = BuildSut();

            await service.ImportAsync(EnterpriseId, Encoding.UTF8.GetBytes(blankNumbers));

            Assert.Equal(["001", "002", "003"], bills.Select(x => x.SupplierNFeDuplicata ?? string.Empty).ToArray());
        }

        /// <summary>The same nota, paid in three instalments: 120.00 then 100.00 twice.</summary>
        private static byte[] BuildInstalmentNota()
        {
            var single = Encoding.UTF8.GetString(BuildNota());
            const string oneDuplicata = """
                        <dup>
                          <nDup>001</nDup>
                          <dVenc>2026-09-09</dVenc>
                          <vDup>320.00</vDup>
                        </dup>
                """;

            const string threeDuplicatas = """
                        <dup>
                          <nDup>001</nDup>
                          <dVenc>2026-09-09</dVenc>
                          <vDup>120.00</vDup>
                        </dup>
                        <dup>
                          <nDup>002</nDup>
                          <dVenc>2026-10-09</dVenc>
                          <vDup>100.00</vDup>
                        </dup>
                        <dup>
                          <nDup>003</nDup>
                          <dVenc>2026-11-09</dVenc>
                          <vDup>100.00</vDup>
                        </dup>
                """;

            Assert.Contains(oneDuplicata, single);
            return Encoding.UTF8.GetBytes(single.Replace(oneDuplicata, threeDuplicatas));
        }

        [Fact]
        public async Task Import_DoesNotMoveStock()
        {
            // A nota is issued when goods ship. MarkProductsReceived is still the arrival event.
            var product = BuildProduct("Plugsuit", ean: "7891234567895");
            var (service, _, _) = BuildSut(products: [product]);

            await service.ImportAsync(EnterpriseId, BuildNota());

            Assert.Equal(10m, product.StorageQuantity);
        }

        [Fact]
        public async Task Import_TheSameFileTwice_ReturnsTheBillThatAlreadyExists()
        {
            var product = BuildProduct("Plugsuit", ean: "7891234567895");
            var (service, bills, suppliers) = BuildSut(products: [product]);

            var first = await service.ImportAsync(EnterpriseId, BuildNota());
            var second = await service.ImportAsync(EnterpriseId, BuildNota());

            Assert.Single(bills);
            Assert.Single(suppliers);
            Assert.Equal(first.PayableBillId, second.PayableBillId);
            Assert.True(second.AlreadyImported);
        }

        [Fact]
        public async Task Import_ReportsALineItCannotMap_AndNeverInventsAProduct()
        {
            var product = BuildProduct("Plugsuit", ean: "7891234567895");
            var (service, _, _) = BuildSut(products: [product]);

            var result = await service.ImportAsync(EnterpriseId, BuildNota());

            var unmatched = Assert.Single(result.UnmatchedItems);
            Assert.Equal("PROG-02", unmatched.Code);
            Assert.Equal("Entry Plug Coolant", unmatched.Description);
            Assert.Equal(1, result.MatchedItemCount);
        }

        [Fact]
        public async Task Import_MatchesOnTheDescription_WhenTheLineHasNoBarcode()
        {
            var coolant = BuildProduct("Entry Plug Coolant", ean: null);
            var (service, _, _) = BuildSut(products: [coolant]);

            var result = await service.ImportAsync(EnterpriseId, BuildNota());

            Assert.Equal(1, result.MatchedItemCount);
            Assert.Equal("PLUG-01", Assert.Single(result.UnmatchedItems).Code);
        }

        [Fact]
        public async Task Import_ReusesAKnownSupplier()
        {
            var supplier = new Supplier
            {
                Id = Guid.NewGuid(),
                EnterpriseId = EnterpriseId,
                Name = "Nerv Supplies",
                Document = EmitterCnpj,
                IsActive = true
            };

            var (service, bills, suppliers) = BuildSut(suppliers: [supplier]);

            var result = await service.ImportAsync(EnterpriseId, BuildNota());

            Assert.Single(suppliers);
            Assert.False(result.SupplierCreated);
            Assert.Equal(supplier.Id, Assert.Single(bills).SupplierId);
        }

        [Fact]
        public async Task Import_WithoutADuplicata_FallsBackToThirtyDays()
        {
            var withoutCobr = Encoding.UTF8.GetString(BuildNota());
            var start = withoutCobr.IndexOf("<cobr>", StringComparison.Ordinal);
            var end = withoutCobr.IndexOf("</cobr>", StringComparison.Ordinal) + "</cobr>".Length;
            withoutCobr = withoutCobr.Remove(start, end - start);

            var (service, bills, _) = BuildSut();

            await service.ImportAsync(EnterpriseId, Encoding.UTF8.GetBytes(withoutCobr));

            Assert.Equal(new DateTime(2026, 9, 9), Assert.Single(bills).DueDate);
        }

        [Fact]
        public async Task Import_MergesTwoLinesOfTheSameProduct()
        {
            // The payable's item index is unique per (bill, product), so a nota that lists the same
            // product twice must not be refused.
            var product = BuildProduct("Plugsuit", ean: "7891234567895");
            var doubled = Encoding.UTF8.GetString(BuildNota()).Replace("PROG-02", "PLUG-01")
                .Replace("<cEAN>SEM GTIN</cEAN>", "<cEAN>7891234567895</cEAN>")
                .Replace("<xProd>Entry Plug Coolant</xProd>", "<xProd>Plugsuit</xProd>");

            var (service, _, _) = BuildSut(products: [product]);

            var result = await service.ImportAsync(EnterpriseId, Encoding.UTF8.GetBytes(doubled));

            Assert.Equal(1, result.MatchedItemCount);
            Assert.Empty(result.UnmatchedItems);
        }

        #endregion

        private static Product BuildProduct(string name, string? ean) => new()
        {
            Id = Guid.NewGuid(),
            EnterpriseId = EnterpriseId,
            Name = name,
            Description = name,
            UnitOfMeasure = nameof(EnumUnitOfMeasure.Unit),
            StorageQuantity = 10m,
            DefaultValue = 50m,
            Cean = ean,
            IsActive = true
        };

        private static (SupplierNFeImportService Service, List<PayableBill> Bills, List<Supplier> Suppliers)
            BuildSut(List<Product>? products = null, List<Supplier>? suppliers = null)
        {
            var billStore = new List<PayableBill>();
            var supplierStore = suppliers ?? [];
            var productStore = products ?? [];

            var payableBillService = new Mock<IPayableBillService>();
            payableBillService
                .Setup(x => x.CreateAsync(It.IsAny<PayableBill>(), It.IsAny<bool>(), It.IsAny<bool>()))
                .ReturnsAsync((PayableBill bill, bool _, bool __) =>
                {
                    bill.Id = bill.Id == Guid.Empty ? Guid.NewGuid() : bill.Id;
                    bill.IsActive = true;
                    billStore.Add(bill);
                    return bill;
                });

            var service = new SupplierNFeImportService(
                Repo(billStore).Object,
                Repo(supplierStore).Object,
                Repo(productStore).Object,
                payableBillService.Object);

            return (service, billStore, supplierStore);
        }

        private static Mock<IRepository<TEntity>> Repo<TEntity>(List<TEntity> records)
            where TEntity : BaseEntity
        {
            var repo = new Mock<IRepository<TEntity>>();

            repo.Setup(x => x.GetAllAsync(It.IsAny<Expression<Func<TEntity, bool>>?>()))
                .ReturnsAsync((Expression<Func<TEntity, bool>>? predicate) => predicate == null
                    ? records
                    : records.Where(predicate.Compile()).ToList());

            repo.Setup(x => x.GetPagedAsync(
                    It.IsAny<Expression<Func<TEntity, bool>>?>(),
                    It.IsAny<Func<IQueryable<TEntity>, IOrderedQueryable<TEntity>>?>(),
                    It.IsAny<int?>(),
                    It.IsAny<int?>()))
                .ReturnsAsync((
                    Expression<Func<TEntity, bool>>? predicate,
                    Func<IQueryable<TEntity>, IOrderedQueryable<TEntity>>? _,
                    int? __,
                    int? ___) =>
                {
                    var items = predicate == null ? records.ToList() : records.Where(predicate.Compile()).ToList();
                    return ((IReadOnlyList<TEntity>)items, items.Count);
                });

            repo.Setup(x => x.CreateAsync(It.IsAny<TEntity>()))
                .ReturnsAsync((TEntity entity) =>
                {
                    records.Add(entity);
                    return entity;
                });

            repo.Setup(x => x.CommitAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

            return repo;
        }

        /// <summary>
        /// One authorized nota: two lines totalling 300.00, 20.00 of freight, vNF 320.00, one
        /// duplicata thirty days out.
        /// </summary>
        private static byte[] BuildNota() => Encoding.UTF8.GetBytes($"""
            <?xml version="1.0" encoding="UTF-8"?>
            <nfeProc versao="4.00" xmlns="http://www.portalfiscal.inf.br/nfe">
              <NFe>
                <infNFe versao="4.00" Id="NFe{AccessKey}">
                  <ide>
                    <cUF>35</cUF>
                    <cNF>00000042</cNF>
                    <natOp>VENDA DE MERCADORIA</natOp>
                    <mod>55</mod>
                    <serie>1</serie>
                    <nNF>42</nNF>
                    <dhEmi>2026-08-10T10:00:00-03:00</dhEmi>
                    <tpNF>1</tpNF>
                    <idDest>1</idDest>
                    <cMunFG>3550308</cMunFG>
                    <tpImp>1</tpImp>
                    <tpEmis>1</tpEmis>
                    <cDV>8</cDV>
                    <tpAmb>1</tpAmb>
                    <finNFe>1</finNFe>
                    <indFinal>0</indFinal>
                    <indPres>9</indPres>
                    <procEmi>0</procEmi>
                    <verProc>1.0</verProc>
                  </ide>
                  <emit>
                    <CNPJ>{EmitterCnpj}</CNPJ>
                    <xNome>Nerv Supplies LTDA</xNome>
                    <xFant>Nerv Supplies</xFant>
                    <enderEmit>
                      <xLgr>Rua Geofront</xLgr>
                      <nro>1</nro>
                      <xBairro>Centro</xBairro>
                      <cMun>3550308</cMun>
                      <xMun>Tokyo-3</xMun>
                      <UF>SP</UF>
                      <CEP>01001000</CEP>
                    </enderEmit>
                    <IE>110042490114</IE>
                    <CRT>3</CRT>
                  </emit>
                  <dest>
                    <CNPJ>99999999000191</CNPJ>
                    <xNome>Cliente</xNome>
                    <indIEDest>9</indIEDest>
                  </dest>
                  <det nItem="1">
                    <prod>
                      <cProd>PLUG-01</cProd>
                      <cEAN>7891234567895</cEAN>
                      <xProd>Plugsuit</xProd>
                      <NCM>61099000</NCM>
                      <CFOP>5102</CFOP>
                      <uCom>UN</uCom>
                      <qCom>3.0000</qCom>
                      <vUnCom>60.0000000000</vUnCom>
                      <vProd>180.00</vProd>
                      <indTot>1</indTot>
                    </prod>
                  </det>
                  <det nItem="2">
                    <prod>
                      <cProd>PROG-02</cProd>
                      <cEAN>SEM GTIN</cEAN>
                      <xProd>Entry Plug Coolant</xProd>
                      <NCM>38200000</NCM>
                      <CFOP>5102</CFOP>
                      <uCom>LT</uCom>
                      <qCom>4.0000</qCom>
                      <vUnCom>30.0000000000</vUnCom>
                      <vProd>120.00</vProd>
                      <indTot>1</indTot>
                    </prod>
                  </det>
                  <total>
                    <ICMSTot>
                      <vBC>300.00</vBC>
                      <vICMS>0.00</vICMS>
                      <vProd>300.00</vProd>
                      <vFrete>20.00</vFrete>
                      <vDesc>0.00</vDesc>
                      <vNF>320.00</vNF>
                    </ICMSTot>
                  </total>
                  <cobr>
                    <fat>
                      <nFat>42</nFat>
                      <vOrig>320.00</vOrig>
                      <vLiq>320.00</vLiq>
                    </fat>
                    <dup>
                      <nDup>001</nDup>
                      <dVenc>2026-09-09</dVenc>
                      <vDup>320.00</vDup>
                    </dup>
                  </cobr>
                </infNFe>
              </NFe>
              <protNFe versao="4.00">
                <infProt>
                  <tpAmb>1</tpAmb>
                  <verAplic>SP_NFE_PL009</verAplic>
                  <chNFe>{AccessKey}</chNFe>
                  <dhRecbto>2026-08-10T10:05:00-03:00</dhRecbto>
                  <nProt>135260000000001</nProt>
                  <cStat>100</cStat>
                  <xMotivo>Autorizado o uso da NF-e</xMotivo>
                </infProt>
              </protNFe>
            </nfeProc>
            """);
    }
}
