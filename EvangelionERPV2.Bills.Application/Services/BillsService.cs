using BoletoNetCore;
using EvangelionERPV2.BillsModule.Application.Configs;
using EvangelionERPV2.BillsModule.Application.Interface;
using EvangelionERPV2.BillsModule.Domain.Interface;
using EvangelionERPV2.Shared.Entities;
using EvangelionERPV2.Shared.Exceptions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PuppeteerSharp;
using PuppeteerSharp.Media;
using Serilog;
using BillEntity = EvangelionERPV2.Shared.Entities.Bill;

namespace EvangelionERPV2.BillsModule.Application.Services
{
    public class BillsService : IBillsService<BillEntity>, IDisposable
    {
        private readonly EvangelionERPV2.Shared.Repositories.IRepository<BillEntity> _billRepository;
        private readonly IBillsRepository<BillEntity> _billRepositoryCustom;
        private readonly EvangelionERPV2.Shared.Repositories.IRepository<Order> _orderRepository;
        private readonly EvangelionERPV2.Shared.Repositories.IRepository<Customer> _customerRepository;
        private readonly BillsSettings _settings;
        private bool disposed;
        private static IBrowser? sharedBrowser;
        private static readonly SemaphoreSlim browserLock = new(1, 1);

        public BillsService(
            EvangelionERPV2.Shared.Repositories.IRepository<BillEntity> billRepository,
            IBillsRepository<BillEntity> billRepositoryCustom,
            EvangelionERPV2.Shared.Repositories.IRepository<Order> orderRepository,
            EvangelionERPV2.Shared.Repositories.IRepository<Customer> customerRepository,
            IOptions<BillsSettings> settings)
        {
            _billRepository = billRepository;
            _billRepositoryCustom = billRepositoryCustom;
            _orderRepository = orderRepository;
            _customerRepository = customerRepository;
            _settings = settings?.Value ?? new BillsSettings();
        }

        public async Task<BillEntity?> GetByOrderIdAsync(Guid orderId)
        {
            if (orderId == Guid.Empty)
                return null;

            return await _billRepositoryCustom.GetByOrderIdAsync(orderId);
        }

        public async Task<BillEntity?> GenerateAsync(Guid orderId)
        {
            if (!_settings.Enabled)
            {
                Log.Logger.Information("Bill generation skipped: disabled in settings.");
                return null;
            }

            if (orderId == Guid.Empty)
                throw new InsertDatabaseException("Order Id is invalid for bill generation.");

            var existing = await _billRepositoryCustom.GetByOrderIdAsync(orderId);
            if (existing != null)
                return existing;

            var order = await _orderRepository.GetByIdAsync(orderId);
            if (order == null)
                throw new NotFoundDatabaseException($"{nameof(Order)} was not found.");

            var customer = order.CustomerId.HasValue ? await _customerRepository.GetByIdAsync(order.CustomerId.Value) : null;
            var billNet = BuildBill(order, customer);

            var billBancario = new BoletoBancario
            {
                Boleto = billNet,
                Banco = billNet.Banco,
                MostrarEnderecoBeneficiario = true
            };

            var html = billBancario.MontaHtmlEmbedded();

            var bill = new BillEntity
            {
                Id = Guid.NewGuid(),
                OrderId = order.Id,
                BankCode = billNet.Banco.Codigo,
                OurNumber = billNet.NossoNumero,
                DocumentNumber = billNet.NumeroDocumento,
                IssueDate = billNet.DataEmissao,
                DueDate = billNet.DataVencimento,
                Amount = (double)billNet.ValorTitulo,
                DigitableLine = billNet.CodigoBarra.LinhaDigitavel,
                BarCode = billNet.CodigoBarra.CodigoDeBarras,
                HtmlContent = html
            };

            await _billRepository.CreateAsync(bill);
            try
            {
                await _billRepository.CommitAsync();
            }
            catch (DbUpdateException commitException)
            {
                var concurrentBill = await _billRepositoryCustom.GetByOrderIdAsync(orderId);
                if (concurrentBill == null)
                    throw;

                Log.Logger.Warning(
                    commitException,
                    "Bill generation duplicate detected for order {OrderId}. Returning existing bill {BillId}.",
                    orderId,
                    concurrentBill.Id);

                return concurrentBill;
            }

            return bill;
        }

        public async Task<byte[]?> GetPdfAsync(Guid orderId)
        {
            if (orderId == Guid.Empty)
                return null;

            var existing = await _billRepositoryCustom.GetByOrderIdAsync(orderId);
            if (existing == null)
            {
                if (!_settings.Enabled)
                    return null;

                existing = await GenerateAsync(orderId);
            }

            if (existing == null)
                return null;

            var html = existing.HtmlContent;
            if (string.IsNullOrWhiteSpace(html))
            {
                var order = await _orderRepository.GetByIdAsync(orderId);
                if (order == null)
                    throw new NotFoundDatabaseException($"{nameof(Order)} was not found.");

                var customer = order.CustomerId.HasValue
                    ? await _customerRepository.GetByIdAsync(order.CustomerId.Value)
                    : null;
                var billNet = BuildBill(order, customer);
                var billBancario = new BoletoBancario
                {
                    Boleto = billNet,
                    Banco = billNet.Banco,
                    MostrarEnderecoBeneficiario = true
                };
                html = billBancario.MontaHtmlEmbedded();
            }

            return await RenderPdfAsync(html);
        }

        private static async Task<byte[]> RenderPdfAsync(string html)
        {
            var browser = await GetBrowserAsync();
            await using var page = await browser.NewPageAsync();
            await page.EmulateMediaTypeAsync(MediaType.Screen);
            await page.SetContentAsync(html, new NavigationOptions
            {
                WaitUntil = new[] { WaitUntilNavigation.Load }
            });
            var pdf = await page.PdfDataAsync(new PdfOptions
            {
                Format = PaperFormat.A4,
                PrintBackground = true,
                PreferCSSPageSize = true,
                MarginOptions = new MarginOptions
                {
                    Top = "0cm",
                    Bottom = "0cm",
                    Left = "0cm",
                    Right = "0cm"
                }
            });
            await page.CloseAsync();
            return pdf;
        }

        private static async Task<IBrowser> GetBrowserAsync()
        {
            if (sharedBrowser != null)
                return sharedBrowser;

            await browserLock.WaitAsync();
            try
            {
                if (sharedBrowser != null)
                    return sharedBrowser;

                var fetcher = new BrowserFetcher();
                await fetcher.DownloadAsync();

                sharedBrowser = await Puppeteer.LaunchAsync(new LaunchOptions
                {
                    Headless = true,
                    Args = new[] { "--no-sandbox", "--disable-setuid-sandbox" }
                });
            }
            finally
            {
                browserLock.Release();
            }

            return sharedBrowser;
        }

        private BoletoNetCore.Boleto BuildBill(Order order, Customer? customer)
        {
            ValidateSettings();

            var conta = new ContaBancaria
            {
                Agencia = _settings.Agency,
                DigitoAgencia = _settings.AgencyDigit,
                Conta = _settings.Account,
                DigitoConta = _settings.AccountDigit,
                CarteiraPadrao = _settings.Wallet,
                VariacaoCarteiraPadrao = NormalizeWalletVariation(_settings.WalletVariation),
                TipoCarteiraPadrao = (TipoCarteira)_settings.WalletType,
                TipoFormaCadastramento = (TipoFormaCadastramento)_settings.RegistrationType,
                TipoImpressaoBoleto = (TipoImpressaoBoleto)_settings.PrintType,
                TipoDocumento = (TipoDocumento)_settings.DocumentType
            };

            var beneficiario = new Beneficiario
            {
                CPFCNPJ = NormalizeDocument(_settings.BeneficiaryDocument),
                Nome = _settings.BeneficiaryName,
                Codigo = _settings.BeneficiaryCode,
                CodigoDV = _settings.BeneficiaryCodeDigit,
                CodigoTransmissao = _settings.TransmissionCode,
                Observacoes = _settings.BeneficiaryNotes,
                ContaBancaria = conta,
                Endereco = new Endereco
                {
                    LogradouroEndereco = _settings.BeneficiaryAddress,
                    LogradouroNumero = _settings.BeneficiaryAddressNumber,
                    LogradouroComplemento = _settings.BeneficiaryAddressComplement,
                    Bairro = _settings.BeneficiaryNeighborhood,
                    Cidade = _settings.BeneficiaryCity,
                    UF = _settings.BeneficiaryState,
                    CEP = _settings.BeneficiaryZipCode
                }
            };

            var banco = Banco.Instancia(_settings.BankCode);
            banco.Beneficiario = beneficiario;
            banco.FormataBeneficiario();

            var pagador = BuildPagador(customer);
            var vencimento = order.Payday ?? order.PaymentScheduledDate;
            var billNetCore = new BoletoNetCore.Boleto(banco)
            {
                DataVencimento = vencimento,
                DataEmissao = DateTime.UtcNow,
                DataProcessamento = DateTime.UtcNow,
                ValorTitulo = Convert.ToDecimal(order.TotalValue),
                NossoNumero = BuildNossoNumero(order),
                NumeroDocumento = BuildDocumentNumber(order),
                EspecieDocumento = TipoEspecieDocumento.DM,
                Pagador = pagador,
                MensagemInstrucoesCaixa = _settings.Instructions ?? string.Empty,
                ImprimirMensagemInstrucao = !string.IsNullOrWhiteSpace(_settings.Instructions)
            };

            billNetCore.ValidarDados();
            return billNetCore;
        }

        private Pagador BuildPagador(Customer? customer)
        {
            var payerDocument = NormalizeDocument(customer?.Document ?? _settings.DefaultPayerDocument);
            var payerName = string.IsNullOrWhiteSpace(customer?.Name) ? _settings.DefaultPayerName : customer.Name;
            var payerAddress = string.IsNullOrWhiteSpace(customer?.Adress) ? _settings.DefaultPayerAddress : customer.Adress;

            if (string.IsNullOrWhiteSpace(payerDocument) || (payerDocument.Length != 11 && payerDocument.Length != 14))
                throw new InsertDatabaseException("Payer document is required with 11 or 14 digits.");

            return new Pagador
            {
                Nome = payerName,
                CPFCNPJ = payerDocument,
                Telefone = customer?.PhoneNumber ?? string.Empty,
                Observacoes = customer?.Email ?? string.Empty,
                Endereco = new Endereco
                {
                    LogradouroEndereco = payerAddress ?? string.Empty
                }
            };
        }

        private string BuildNossoNumero(Order order)
        {
            var raw = $"{order.Id:N}{order.CreatedAt:yyyyMMddHHmmss}";
            var digits = new string(raw.Where(char.IsDigit).ToArray());
            var length = _settings.NossoNumeroLength <= 0 ? 8 : _settings.NossoNumeroLength;

            if (digits.Length < length)
                digits = digits.PadLeft(length, '0');

            return digits.Substring(digits.Length - length, length);
        }

        private static string BuildDocumentNumber(Order order)
        {
            if (order.Id == Guid.Empty)
                return DateTime.UtcNow.Ticks.ToString();

            return order.Id.ToString("N").Substring(0, 10).ToUpperInvariant();
        }

        private void ValidateSettings()
        {
            if (_settings.BankCode <= 0)
                throw new InsertDatabaseException("BillsSettings.BankCode is required.");
            var beneficiaryDocument = NormalizeDocument(_settings.BeneficiaryDocument);
            if (string.IsNullOrWhiteSpace(beneficiaryDocument) || (beneficiaryDocument.Length != 11 && beneficiaryDocument.Length != 14))
                throw new InsertDatabaseException("BillsSettings.BeneficiaryDocument is required.");
            if (string.IsNullOrWhiteSpace(_settings.BeneficiaryName))
                throw new InsertDatabaseException("BillsSettings.BeneficiaryName is required.");
            if (string.IsNullOrWhiteSpace(_settings.Agency))
                throw new InsertDatabaseException("BillsSettings.Agency is required.");
            if (string.IsNullOrWhiteSpace(_settings.Account))
                throw new InsertDatabaseException("BillsSettings.Account is required.");
            if (string.IsNullOrWhiteSpace(_settings.Wallet))
                throw new InsertDatabaseException("BillsSettings.Wallet is required.");
            if (string.IsNullOrWhiteSpace(_settings.DefaultPayerDocument))
                throw new InsertDatabaseException("BillsSettings.DefaultPayerDocument is required.");
        }

        private static string NormalizeDocument(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;

            return new string(value.Where(char.IsDigit).ToArray());
        }

        private static string NormalizeWalletVariation(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;

            var trimmed = value.Trim();
            return trimmed.All(c => c == '0') ? string.Empty : trimmed;
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
                    (_billRepository as IDisposable)?.Dispose();
                    (_billRepositoryCustom as IDisposable)?.Dispose();
                    (_orderRepository as IDisposable)?.Dispose();
                    (_customerRepository as IDisposable)?.Dispose();
                }

                disposed = true;
            }
        }

        ~BillsService()
        {
            Dispose(false);
        }
        #endregion
    }
}

