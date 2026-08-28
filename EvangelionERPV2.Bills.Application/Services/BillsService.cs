using BoletoNetCore;
using EvangelionERPV2.BillsModule.Application.Configs;
using EvangelionERPV2.BillsModule.Application.Interface;
using EvangelionERPV2.BillsModule.Domain.Interface;
using EvangelionERPV2.EmailModule.Application.Interface;
using EvangelionERPV2.Shared.Entities;
using EvangelionERPV2.Shared.Exceptions;
using EvangelionERPV2.Shared.Utils;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Prometheus;
using Serilog;
using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using BillEntity = EvangelionERPV2.Shared.Entities.Bill;

namespace EvangelionERPV2.BillsModule.Application.Services
{
    public class BillsService : IBillsService<BillEntity>, IDisposable
    {
        /// <summary>
        /// Counted, not just logged: a bill that goes out without its PDF is invisible in every
        /// other signal - the mail sends, the request answers 200, nothing retries.
        /// </summary>
        private static readonly Counter MissingBillPdfCounter = Metrics.CreateCounter(
            "evangelion_bill_email_missing_pdf_total",
            "Bill emails sent with no PDF attached, by reason",
            new CounterConfiguration { LabelNames = ["reason"] });

        private readonly EvangelionERPV2.Shared.Repositories.IRepository<BillEntity> _billRepository;
        private readonly IBillsRepository<BillEntity> _billRepositoryCustom;
        private readonly EvangelionERPV2.Shared.Repositories.IRepository<Order> _orderRepository;
        private readonly EvangelionERPV2.Shared.Repositories.IRepository<Customer> _customerRepository;
        private readonly EvangelionERPV2.Shared.Repositories.IRepository<Enterprise> _enterpriseRepository;
        private readonly EvangelionERPV2.Shared.Repositories.IRepository<BillNumberSequence> _sequenceRepository;
        // Resolved lazily: the email stack (RabbitMQ, SMTP settings) must never be able to fail a
        // bill generation request just by being constructed.
        private readonly Func<IEmailService<EmailStructure>> _emailServiceFactory;
        private readonly BillsSettings _settings;
        private bool disposed;

        public BillsService(
            EvangelionERPV2.Shared.Repositories.IRepository<BillEntity> billRepository,
            IBillsRepository<BillEntity> billRepositoryCustom,
            EvangelionERPV2.Shared.Repositories.IRepository<Order> orderRepository,
            EvangelionERPV2.Shared.Repositories.IRepository<Customer> customerRepository,
            EvangelionERPV2.Shared.Repositories.IRepository<Enterprise> enterpriseRepository,
            EvangelionERPV2.Shared.Repositories.IRepository<BillNumberSequence> sequenceRepository,
            Func<IEmailService<EmailStructure>> emailServiceFactory,
            IOptions<BillsSettings> settings)
        {
            _billRepository = billRepository;
            _billRepositoryCustom = billRepositoryCustom;
            _orderRepository = orderRepository;
            _customerRepository = customerRepository;
            _enterpriseRepository = enterpriseRepository;
            _sequenceRepository = sequenceRepository;
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

            // Allocated only after the existing-bill check, so a repeated generate call does not
            // burn a number for a boleto that already exists.
            var billNet = BuildBill(order, customer, await AllocateNossoNumeroAsync(enterpriseId));

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
                HtmlContent = html,
                Status = BillStatus.Registered
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

        public async Task<IReadOnlyList<BillEntity>> GetHistoryAsync(Guid orderId, Guid enterpriseId)
        {
            if (orderId == Guid.Empty)
                return [];

            if (!await IsOrderWithinEnterpriseAsync(orderId, enterpriseId))
                return [];

            return await _billRepositoryCustom.GetAllByOrderIdAsync(orderId);
        }

        /// <summary>
        /// Records a settlement against one boleto. Manual for now — the bank has never heard of
        /// these titles, so nothing else can tell us the money arrived (CNAB retorno is M9). Without
        /// it a customer pays and the ERP never knows, which is what makes the current boleto
        /// commercially useless.
        /// </summary>
        public async Task<BillEntity?> SettleAsync(
            Guid billId, Guid enterpriseId, decimal paidAmount, DateTime? paidAt, string? settlementProtocol)
        {
            if (billId == Guid.Empty || enterpriseId == Guid.Empty)
                return null;

            if (paidAmount <= 0)
                throw new InsertDatabaseException("Paid amount must be greater than zero.");

            var bill = await _billRepository.GetByIdAsync(billId);
            if (bill == null || bill.IsActive == false)
                return null;

            // Bill carries no EnterpriseId of its own; the order it belongs to is the tenant gate.
            if (!await IsOrderWithinEnterpriseAsync(bill.OrderId, enterpriseId))
                return null;

            if (bill.Status == BillStatus.Paid)
                throw new InsertDatabaseException("This bill was already settled.");

            if (bill.Status == BillStatus.Cancelled)
                throw new InsertDatabaseException("A cancelled bill cannot be settled. Settle the boleto that superseded it.");

            var settledAt = paidAt ?? DateTime.UtcNow;

            // A settlement dated in the future would be projected as cash that has not arrived.
            if (settledAt.Date > DateTime.UtcNow.Date)
                throw new InsertDatabaseException("Settlement date cannot be in the future.");

            bill.Status = BillStatus.Paid;
            bill.PaidAt = settledAt;
            bill.PaidAmount = paidAmount;
            bill.SettlementProtocol = string.IsNullOrWhiteSpace(settlementProtocol) ? null : settlementProtocol.Trim();
            bill.UpdatedAt = DateTime.UtcNow;

            _billRepository.Update(bill);
            await _billRepository.CommitAsync();

            return bill;
        }

        /// <summary>
        /// Cancels the order's live boleto and issues a fresh one.
        /// </summary>
        /// <remarks>
        /// Supersede, never mutate: the payer may already be holding the old document, and a
        /// silently edited due date leaves them with a barcode that no longer matches anything.
        /// Cancelling first is also what frees the filtered unique index on OrderId so the new row
        /// can be inserted at all.
        /// </remarks>
        public async Task<BillEntity?> ReissueAsync(Guid orderId, Guid enterpriseId)
        {
            if (orderId == Guid.Empty)
                return null;

            var order = await GetScopedOrderAsync(orderId, enterpriseId);
            if (order == null)
                return null;

            var current = await _billRepositoryCustom.GetByOrderIdAsync(orderId);
            if (current != null)
            {
                if (current.Status == BillStatus.Paid)
                    throw new InsertDatabaseException("A settled bill cannot be reissued.");

                current.Status = BillStatus.Cancelled;
                current.UpdatedAt = DateTime.UtcNow;
                _billRepository.Update(current);
                await _billRepository.CommitAsync();
            }

            return await GenerateAsync(orderId, enterpriseId);
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

        /// <summary>
        /// The one place a bill goes out without its PDF. Deliberately <c>Error</c> and counted:
        /// this is the only silent data-loss path left in the system - the customer receives a mail
        /// that is missing the document it is about, and nothing else would ever say so.
        /// </summary>
        private static void ReportMissingPdf(BillEntity bill, string reason, string errorType)
        {
            MissingBillPdfCounter.WithLabels(reason).Inc();

            Log.Logger.Error(
                "Bill email is going out with no PDF attached. BillId={BillId} Reason={Reason} ErrorType={ErrorType}",
                bill.Id,
                reason,
                errorType);
        }

        private async Task<IEnumerable<EmailAttachment>> BuildBillAttachmentsAsync(BillEntity bill)
        {
            if (!_settings.AttachPdfToBillEmail || string.IsNullOrWhiteSpace(bill.HtmlContent))
                return [];

            byte[] pdf;
            try
            {
                pdf = await HtmlPdfRenderer.RenderPdfAsync(NormalizeEmbeddedImageMimeTypes(bill.HtmlContent));
            }
            catch (Exception ex)
            {
                // No headless browser available: still send the mail, just without the PDF. Sending
                // it is the right call - the digitable line in the body is payable on its own - but
                // it used to happen at Warning with nothing counting it, so a renderer that was
                // broken for a week looked exactly like a renderer that worked.
                ReportMissingPdf(bill, "render_failed", ex.GetType().Name);
                return [];
            }

            if (pdf.Length == 0)
            {
                // A renderer that returns nothing without throwing is the same outage wearing a
                // different hat, and this branch used to be entirely silent.
                ReportMissingPdf(bill, "empty_pdf", "None");
                return [];
            }

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
                // The stored NossoNumero, never a fresh allocation: this branch only re-renders a
                // boleto the payer may already hold, and re-numbering it would make the barcode
                // disagree with the title the bank was told about.
                var billNet = BuildBill(order, customer, existing.OurNumber);
                var billBancario = new BoletoBancario
                {
                    Boleto = billNet,
                    Banco = billNet.Banco,
                    MostrarEnderecoBeneficiario = true
                };
                html = NormalizeEmbeddedImageMimeTypes(billBancario.MontaHtmlEmbedded());
            }

            return await HtmlPdfRenderer.RenderPdfAsync(html);
        }

        private BoletoNetCore.Boleto BuildBill(Order order, Customer? customer, string nossoNumero)
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
                NossoNumero = nossoNumero,
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

        /// <summary>
        /// Hands out the next NossoNumero for this enterprise's (bank, wallet) range, zero-padded to
        /// the contracted field width.
        /// </summary>
        /// <remarks>
        /// This used to be derived — the last N digits of the order GUID plus its creation
        /// timestamp. Two different orders can produce the same N digits, and the value lands
        /// wherever it lands rather than inside the range the convênio grants; a collision at the
        /// bank rejects the title or credits the wrong one. Allocation is the only shape that is
        /// both unique and in-range, so the counter is authoritative and the order id is not used
        /// at all any more.
        /// </remarks>
        private async Task<string> AllocateNossoNumeroAsync(Guid enterpriseId)
        {
            var length = NossoNumeroLength;

            var allocated = await _billRepositoryCustom.AllocateNextNumberAsync(
                enterpriseId, _settings.BankCode, _settings.Wallet);

            if (allocated == null)
            {
                // No row yet, or the range ran out — the statement cannot tell those apart on its
                // own, and they need opposite reactions: one is seeded here, the other must stop
                // issuance loudly rather than emit a number the bank will not honour.
                await EnsureSequenceAsync(enterpriseId);

                allocated = await _billRepositoryCustom.AllocateNextNumberAsync(
                    enterpriseId, _settings.BankCode, _settings.Wallet)
                    ?? throw new InsertDatabaseException(
                        "The NossoNumero range for this bank and wallet is exhausted. Raise BillNumberSequence.Ceiling to the value the convênio contracts.");
            }

            var number = allocated.Value.ToString(CultureInfo.InvariantCulture);

            // Wider than the field the bank reads means a truncated, therefore wrong, title. The
            // ceiling should already have stopped this; belt and braces because the ceiling is
            // operator-configurable.
            if (number.Length > length)
                throw new InsertDatabaseException(
                    $"Allocated NossoNumero {number} does not fit {length} digits. Check BillsSettings.NossoNumeroLength against the convênio.");

            return number.PadLeft(length, '0');
        }

        private int NossoNumeroLength => _settings.NossoNumeroLength <= 0 ? 8 : _settings.NossoNumeroLength;

        /// <summary>
        /// Creates the counter row lazily, off the allocation statement, so allocation stays a
        /// single atomic UPDATE with nothing to create under contention. An existing row is never
        /// reset — a counter that restarted would re-issue numbers already handed to payers.
        /// </summary>
        private async Task EnsureSequenceAsync(Guid enterpriseId)
        {
            var existing = await _sequenceRepository.GetAllAsync(
                x => x.EnterpriseId == enterpriseId
                     && x.BankCode == _settings.BankCode
                     && x.Wallet == _settings.Wallet);

            if (existing.Any())
                return;

            var start = _settings.NossoNumeroStart <= 0 ? 1 : _settings.NossoNumeroStart;
            var ceiling = _settings.NossoNumeroCeiling > 0
                ? _settings.NossoNumeroCeiling
                : WidestNumberFitting(NossoNumeroLength);

            await _sequenceRepository.CreateAsync(new BillNumberSequence
            {
                Id = Guid.NewGuid(),
                EnterpriseId = enterpriseId,
                BankCode = _settings.BankCode,
                Wallet = _settings.Wallet,
                NextNumber = start,
                Ceiling = ceiling,
                CreatedAt = DateTime.UtcNow,
                IsActive = true
            });

            try
            {
                await _sequenceRepository.CommitAsync();
            }
            catch (DbUpdateException)
            {
                // Another instance seeded the same range first. Its row is the one that counts;
                // the caller re-runs the allocation against it.
                Log.Logger.Information(
                    "Bill number sequence for enterprise {EnterpriseId} was seeded concurrently.",
                    enterpriseId);
            }
        }

        /// <summary>The largest value that still fits <paramref name="digits"/> digits, e.g. 8 -> 99999999.</summary>
        private static long WidestNumberFitting(int digits)
        {
            var widest = 1L;
            for (var index = 0; index < digits; index++)
                widest *= 10;

            return widest - 1;
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
                    (_sequenceRepository as IDisposable)?.Dispose();
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
