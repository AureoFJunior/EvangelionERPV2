using BoletoNetCore;
using EvangelionERPV2.BillsModule.Application.Configs;
using EvangelionERPV2.BillsModule.Application.Interface;
using EvangelionERPV2.BillsModule.Domain.Interface;
using EvangelionERPV2.EmailModule.Application.Interface;
using EvangelionERPV2.Shared.Entities;
using EvangelionERPV2.Shared.Exceptions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PuppeteerSharp;
using PuppeteerSharp.Media;
using Serilog;
using System.Net;
using System.Text.RegularExpressions;
using BillEntity = EvangelionERPV2.Shared.Entities.Bill;

namespace EvangelionERPV2.BillsModule.Application.Services
{
    public class BillsService : IBillsService<BillEntity>, IDisposable
    {
        private readonly EvangelionERPV2.Shared.Repositories.IRepository<BillEntity> _billRepository;
        private readonly IBillsRepository<BillEntity> _billRepositoryCustom;
        private readonly EvangelionERPV2.Shared.Repositories.IRepository<Order> _orderRepository;
        private readonly EvangelionERPV2.Shared.Repositories.IRepository<Customer> _customerRepository;
        private readonly EvangelionERPV2.Shared.Repositories.IRepository<Enterprise> _enterpriseRepository;
        // Resolved lazily: the email stack (RabbitMQ, SMTP settings) must never be able to fail a
        // bill generation request just by being constructed.
        private readonly Func<IEmailService<EmailStructure>> _emailServiceFactory;
        private readonly BillsSettings _settings;
        private bool disposed;
        private static IBrowser? sharedBrowser;
        private static readonly SemaphoreSlim browserLock = new(1, 1);

        public BillsService(
            EvangelionERPV2.Shared.Repositories.IRepository<BillEntity> billRepository,
            IBillsRepository<BillEntity> billRepositoryCustom,
            EvangelionERPV2.Shared.Repositories.IRepository<Order> orderRepository,
            EvangelionERPV2.Shared.Repositories.IRepository<Customer> customerRepository,
            EvangelionERPV2.Shared.Repositories.IRepository<Enterprise> enterpriseRepository,
            Func<IEmailService<EmailStructure>> emailServiceFactory,
            IOptions<BillsSettings> settings)
        {
            _billRepository = billRepository;
            _billRepositoryCustom = billRepositoryCustom;
            _orderRepository = orderRepository;
            _customerRepository = customerRepository;
            _enterpriseRepository = enterpriseRepository;
            _emailServiceFactory = emailServiceFactory;
            _settings = settings?.Value ?? new BillsSettings();
        }

        public async Task<BillEntity?> GetByOrderIdAsync(Guid orderId, Guid enterpriseId)
        {
            if (orderId == Guid.Empty)
                return null;

            if (!await IsOrderWithinEnterpriseAsync(orderId, enterpriseId))
                return null;

            return await _billRepositoryCustom.GetByOrderIdAsync(orderId);
        }

        public async Task<BillEntity?> GenerateAsync(Guid orderId, Guid enterpriseId)
        {
            if (!_settings.Enabled)
            {
                Log.Logger.Information("Bill generation skipped: disabled in settings.");
                return null;
            }

            if (orderId == Guid.Empty)
                throw new InsertDatabaseException("Order Id is invalid for bill generation.");

            var order = await GetScopedOrderAsync(orderId, enterpriseId);
            if (order == null)
                throw new NotFoundDatabaseException($"{nameof(Order)} was not found.");

            var existing = await _billRepositoryCustom.GetByOrderIdAsync(orderId);
            if (existing != null)
                return existing;

            var customer = order.CustomerId.HasValue ? await _customerRepository.GetByIdAsync(order.CustomerId.Value) : null;
            var billNet = BuildBill(order, customer);

            var billBancario = new BoletoBancario
            {
                Boleto = billNet,
                Banco = billNet.Banco,
                MostrarEnderecoBeneficiario = true
            };

            var html = NormalizeEmbeddedImageMimeTypes(billBancario.MontaHtmlEmbedded());

            var bill = new BillEntity
            {
                Id = Guid.NewGuid(),
                OrderId = order.Id,
                BankCode = billNet.Banco.Codigo,
                OurNumber = billNet.NossoNumero,
                DocumentNumber = billNet.NumeroDocumento,
                IssueDate = billNet.DataEmissao,
                DueDate = billNet.DataVencimento,
                Amount = billNet.ValorTitulo,
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

            await TrySendBillEmailAsync(bill, order, customer);

            return bill;
        }

        /// <summary>
        /// Enqueues the generated bill to the customer through the email queue.
        /// Failures never block bill generation.
        /// </summary>
        private async Task TrySendBillEmailAsync(BillEntity bill, Order order, Customer? customer)
        {
            try
            {
                var recipient = (customer?.Email ?? string.Empty).Trim();
                if (string.IsNullOrWhiteSpace(recipient) || !order.EnterpriseId.HasValue)
                    return;

                var enterprise = await _enterpriseRepository.GetByIdAsync(order.EnterpriseId.Value);
                if (enterprise == null)
                    return;

                var email = new EmailStructure(
                    BuildBillEmailBody(bill),
                    $"Boleto - Documento {bill.DocumentNumber}",
                    new List<string> { recipient })
                {
                    // Mail clients strip data: URIs, so the rendered bill only survives as an
                    // attachment. The body carries the digitable line as the readable fallback.
                    Attachments = await BuildBillAttachmentsAsync(bill)
                };

                await _emailServiceFactory().SendManualEmail(email, enterprise);
            }
            catch (Exception ex)
            {
                Log.Logger.Error(
                    "Couldn't enqueue bill email. BillId={BillId} ErrorType={ErrorType}",
                    bill.Id,
                    ex.GetType().Name);
            }
        }

        private static string BuildBillEmailBody(BillEntity bill)
        {
            var summary = $"""
                <p>Segue o boleto do documento <strong>{WebUtility.HtmlEncode(bill.DocumentNumber)}</strong>.</p>
                <p>Vencimento: <strong>{bill.DueDate:dd/MM/yyyy}</strong><br/>
                Valor: <strong>{bill.Amount:N2}</strong></p>
                <p>Linha digitável:<br/><strong>{WebUtility.HtmlEncode(bill.DigitableLine)}</strong></p>
                <hr/>
                """;

            // The full bill, barcode included, so it can be paid straight from the email. The email
            // layer turns its embedded images into cid: resources that mail clients render.
            return summary + NormalizeEmbeddedImageMimeTypes(bill.HtmlContent ?? string.Empty);
        }

        private async Task<IEnumerable<EmailAttachment>> BuildBillAttachmentsAsync(BillEntity bill)
        {
            if (!_settings.AttachPdfToBillEmail || string.IsNullOrWhiteSpace(bill.HtmlContent))
                return [];

            byte[] pdf;
            try
            {
                pdf = await RenderPdfAsync(NormalizeEmbeddedImageMimeTypes(bill.HtmlContent));
            }
            catch (Exception ex)
            {
                // No headless browser available: still send the mail, just without the PDF.
                Log.Logger.Warning(
                    "Couldn't render the bill PDF for email. BillId={BillId} ErrorType={ErrorType}",
                    bill.Id,
                    ex.GetType().Name);
                return [];
            }

            if (pdf.Length == 0)
                return [];

            return
            [
                new EmailAttachment
                {
                    FileName = $"boleto-{bill.DocumentNumber}.pdf",
                    ContentType = "application/pdf",
                    Content = pdf
                }
            ];
        }

        /// <summary>
        /// BoletoNetCore labels the barcode data URI as image/gif while writing JPEG bytes, and
        /// strict renderers drop the mismatched image. Relabel each data URI from its real magic
        /// bytes.
        /// </summary>
        private static string NormalizeEmbeddedImageMimeTypes(string html)
        {
            if (string.IsNullOrEmpty(html))
                return html;

            return Regex.Replace(
                html,
                @"data:image/(?<declared>[a-zA-Z0-9.+-]+);base64,(?<payload>[A-Za-z0-9+/=]+)",
                match =>
                {
                    var payload = match.Groups["payload"].Value;
                    var actual = DetectImageMimeType(payload);

                    return actual == null || string.Equals(actual, $"image/{match.Groups["declared"].Value}", StringComparison.OrdinalIgnoreCase)
                        ? match.Value
                        : $"data:{actual};base64,{payload}";
                },
                RegexOptions.None,
                TimeSpan.FromSeconds(5));
        }

        private static string? DetectImageMimeType(string base64Payload)
        {
            // A 12-byte prefix is enough for every magic number below, and base64 decodes in
            // 4-character blocks.
            var prefix = base64Payload.Length <= 16 ? base64Payload : base64Payload[..16];
            prefix = prefix[..(prefix.Length - prefix.Length % 4)];

            byte[] header;
            try
            {
                header = Convert.FromBase64String(prefix);
            }
            catch (FormatException)
            {
                return null;
            }

            if (header.Length >= 3 && header[0] == 0xFF && header[1] == 0xD8 && header[2] == 0xFF)
                return "image/jpeg";

            if (header.Length >= 8 && header[0] == 0x89 && header[1] == 0x50 && header[2] == 0x4E && header[3] == 0x47)
                return "image/png";

            if (header.Length >= 6 && header[0] == 0x47 && header[1] == 0x49 && header[2] == 0x46)
                return "image/gif";

            return null;
        }

        public async Task<byte[]?> GetPdfAsync(Guid orderId, Guid enterpriseId)
        {
            if (orderId == Guid.Empty)
                return null;

            var order = await GetScopedOrderAsync(orderId, enterpriseId);
            if (order == null)
                return null;

            var existing = await _billRepositoryCustom.GetByOrderIdAsync(orderId);
            if (existing == null)
            {
                if (!_settings.Enabled)
                    return null;

                existing = await GenerateAsync(orderId, enterpriseId);
            }

            if (existing == null)
                return null;

            // Bills stored before the relabelling fix still carry the mismatched barcode MIME.
            var html = NormalizeEmbeddedImageMimeTypes(existing.HtmlContent);
            if (string.IsNullOrWhiteSpace(html))
            {
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
                html = NormalizeEmbeddedImageMimeTypes(billBancario.MontaHtmlEmbedded());
            }

            return await RenderPdfAsync(html);
        }

        private static async Task<byte[]> RenderPdfAsync(string html)
        {
            var browser = await GetBrowserAsync();
            await using var page = await browser.NewPageAsync();
            await page.SetJavaScriptEnabledAsync(false);
            await page.SetRequestInterceptionAsync(true);
            page.Request += async (_, eventArgs) =>
            {
                if (ShouldBlockBrowserRequest(eventArgs.Request?.Url))
                {
                    await eventArgs.Request!.AbortAsync();
                    return;
                }

                await eventArgs.Request!.ContinueAsync();
            };
            await page.EmulateMediaTypeAsync(MediaType.Screen);
            await page.SetContentAsync(html, new SetContentOptions
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

        private static bool ShouldBlockBrowserRequest(string? requestUrl)
        {
            var normalized = (requestUrl ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(normalized))
                return true;

            if (normalized.StartsWith("about:", StringComparison.OrdinalIgnoreCase))
                return false;

            if (normalized.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
                return false;

            if (!Uri.TryCreate(normalized, UriKind.Absolute, out var parsedUri))
                return true;

            return parsedUri.Scheme.Equals(Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) ||
                   parsedUri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ||
                   parsedUri.Scheme.Equals(Uri.UriSchemeFile, StringComparison.OrdinalIgnoreCase) ||
                   parsedUri.Scheme.Equals("ftp", StringComparison.OrdinalIgnoreCase) ||
                   parsedUri.Scheme.Equals("ws", StringComparison.OrdinalIgnoreCase) ||
                   parsedUri.Scheme.Equals("wss", StringComparison.OrdinalIgnoreCase);
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

                var executablePath = Environment.GetEnvironmentVariable("PUPPETEER_EXECUTABLE_PATH");
                if (string.IsNullOrWhiteSpace(executablePath))
                {
                    var fetcher = new BrowserFetcher();
                    await fetcher.DownloadAsync();
                }

                sharedBrowser = await Puppeteer.LaunchAsync(new LaunchOptions
                {
                    Headless = true,
                    ExecutablePath = string.IsNullOrWhiteSpace(executablePath) ? null : executablePath,
                    // /dev/shm is 64 MB in a default container and Chromium crashes when it fills.
                    Args = ["--no-sandbox", "--disable-setuid-sandbox", "--disable-dev-shm-usage"]
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

        private Pagador BuildPagador(Customer? customer)
        {
            var payerDocument = NormalizeDocument(customer?.Document ?? _settings.DefaultPayerDocument);
            var payerName = SanitizeHtmlField(string.IsNullOrWhiteSpace(customer?.Name) ? _settings.DefaultPayerName : customer.Name);
            var payerAddress = SanitizeHtmlField(string.IsNullOrWhiteSpace(customer?.Adress) ? _settings.DefaultPayerAddress : customer.Adress);

            if (string.IsNullOrWhiteSpace(payerDocument) || (payerDocument.Length != 11 && payerDocument.Length != 14))
                throw new InsertDatabaseException("Payer document is required with 11 or 14 digits.");

            return new Pagador
            {
                Nome = payerName,
                CPFCNPJ = payerDocument,
                Telefone = SanitizeHtmlField(customer?.PhoneNumber ?? string.Empty),
                Observacoes = SanitizeHtmlField(customer?.Email ?? string.Empty),
                Endereco = new Endereco
                {
                    LogradouroEndereco = payerAddress ?? string.Empty
                }
            };
        }

        private static string SanitizeHtmlField(string value)
        {
            return WebUtility.HtmlEncode((value ?? string.Empty).Trim());
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
                    (_enterpriseRepository as IDisposable)?.Dispose();
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
