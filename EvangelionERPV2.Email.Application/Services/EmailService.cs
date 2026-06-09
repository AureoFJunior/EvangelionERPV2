using EvangelionERPV2.EmailModule.Application.Interface;
using EvangelionERPV2.EnterpriseModule.Domain.Interface;
using EvangelionERPV2.OrderModule.Application.Interface;
using EvangelionERPV2.Shared.Entities;
using EvangelionERPV2.Shared.Exceptions;
using EvangelionERPV2.Shared.Utils;
using MimeKit;
using MimeKit.Text;
using Serilog;
using MailKit.Security;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Util.Store;
using Microsoft.Extensions.Configuration;
using EvangelionERPV2.ProductModule.Application.Interface;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Net;
using System.Net.Sockets;

namespace EvangelionERPV2.EmailModule.Application.Services
{
    public class EmailService : IEmailService<EmailStructure>
    {
        private readonly EvangelionERPV2.Shared.Repositories.IRepository<Enterprise> _enterpriseRepository;
        private readonly EvangelionERPV2.Shared.Repositories.IRepository<Email> _emailRepository;
        public readonly IEmailRabbitMQManager _rabbitMQManager;
        public readonly IOrderService<Order> _orderService;
        public readonly IProductService<Product> _productService;
        private readonly AWSKMSKeyProvider _kmsProvider;
        private readonly IConfiguration _configuration;
        private bool disposed;
        private const string AllowPrivateSmtpDestinationsConfigKey = "Security:AllowPrivateSmtpDestinations";
        private const string EnableInteractiveGoogleOAuthFallbackConfigKey = "EmailSettings:EnableInteractiveGoogleOAuthFallback";

        public EmailService(
            IEmailRabbitMQManager rabbitMQManager,
            EvangelionERPV2.Shared.Repositories.IRepository<Enterprise> enterpriseRepository,
            IOrderService<Order> orderService,
            IProductService<Product> productService,
            EvangelionERPV2.Shared.Repositories.IRepository<Email> emailRepository,
            AWSKMSKeyProvider kmsProvider,
            IConfiguration configuration)
        {
            _rabbitMQManager = rabbitMQManager;
            _enterpriseRepository = enterpriseRepository;
            _orderService = orderService;
            _productService = productService;
            _emailRepository = emailRepository;
            _kmsProvider = kmsProvider;
            _configuration = configuration;
        }

        public async Task<Email> CreateAsync(Email email)
        {
            try
            {
                if (email == null)
                    throw new InsertDatabaseException($"{nameof(Email)} is null");
                if (!email.EnterpriseId.HasValue || email.EnterpriseId.Value == Guid.Empty)
                    throw new InsertDatabaseException("Email enterprise is required.");

                email.Id = Guid.NewGuid();
                email.Password = SharedFunctions.Encrypt(email.Password);

                Email includedEmail = new Email();
                includedEmail = await _emailRepository.CreateAsync(email);
                await _emailRepository.CommitAsync();
                return includedEmail;

            }
            catch (Exception ex)
            {
                throw new InsertDatabaseException("Unexpected error while creating email settings.", ex);
            }
        }

        public async Task<MimeMessage> CreateEmail(EmailStructure email, Guid? enterpriseId = null)
        {
            try
            {
                // Create the Email object

                var message = new MimeMessage();
                var emailSettings = (await ResolveEmailSettingsCandidatesAsync(enterpriseId)).FirstOrDefault();
                if (emailSettings == null)
                    throw new NotFoundDatabaseException("Email settings were not found.");

                message.From.Add(new MailboxAddress(emailSettings.UserName, emailSettings.UserName));

                foreach (var recipientEmail in email.RecipientEmails)
                {
                    message.To.Add(MailboxAddress.Parse(recipientEmail));
                }

                message.Subject = email.Subject;
                message.Body = new TextPart(TextFormat.Html)
                {
                    Text = email.Body
                };

                return message;

            }
            catch (Exception ex)
            {
                Log.Logger.Error("Error while creating email. ErrorType={ErrorType}", GetSafeExceptionType(ex));
                throw new EmailSenderException();
            }
        }

        public async Task SendEmail(MimeMessage message, Guid? enterpriseId = null)
        {
            try
            {
                var settingsCandidates = (await ResolveEmailSettingsCandidatesAsync(enterpriseId)).ToList();
                if (settingsCandidates.Count == 0)
                    throw new InvalidOperationException("Email settings are not configured.");

                string? lastFailure = null;
                foreach (var emailSettings in settingsCandidates)
                {
                    string userEmail = emailSettings.UserName;
                    if (string.IsNullOrWhiteSpace(emailSettings.HostName) || string.IsNullOrWhiteSpace(userEmail))
                        continue;

                    try
                    {
                        var resolvedPort = emailSettings.Port == 0 ? 587 : emailSettings.Port;
                        using var smtpClient = new MailKit.Net.Smtp.SmtpClient();
                        await ConnectToAllowedSmtpDestinationAsync(smtpClient, emailSettings.HostName, resolvedPort);

                        var isAuthenticated = false;
                        string? authFailureReason = null;

                        foreach (var smtpPassword in ResolveEmailPasswordCandidates(emailSettings.Password))
                        {
                            try
                            {
                                await smtpClient.AuthenticateAsync(userEmail, smtpPassword);
                                isAuthenticated = true;
                                break;
                            }
                            catch (Exception ex)
                            {
                                authFailureReason = GetSafeExceptionType(ex);
                                Log.Logger.Warning(
                                    "SMTP authentication failed for sender EmailId={EmailId}. Trying next available authentication option. ErrorType={ErrorType}",
                                    GetLogSafeEmailIdentifier(userEmail),
                                    authFailureReason);
                            }
                        }

                        if (!isAuthenticated)
                        {
                            if (ShouldAttemptInteractiveGoogleOAuthFallback())
                            {
                                isAuthenticated = await TryAuthenticateWithGoogleOAuthAsync(smtpClient, userEmail);
                            }
                            else
                            {
                                Log.Logger.Warning(
                                    "Interactive Google OAuth SMTP fallback is disabled for this runtime. Configure SMTP credentials for sender EmailId={EmailId}.",
                                    GetLogSafeEmailIdentifier(userEmail));
                            }

                            if (!isAuthenticated)
                            {
                                var reasonSuffix = string.IsNullOrWhiteSpace(authFailureReason) ? string.Empty : $" ErrorType={authFailureReason}";
                                throw new InvalidOperationException($"SMTP authentication failed for sender EmailId={GetLogSafeEmailIdentifier(userEmail)}.{reasonSuffix}");
                            }
                        }

                        // Ensure visible sender is consistent with the authenticated account.
                        message.From.Clear();
                        message.From.Add(new MailboxAddress(userEmail, userEmail));

                        Log.Logger.Information("Sending emails");

                        await smtpClient.SendAsync(message);
                        await smtpClient.DisconnectAsync(true);

                        Log.Logger.Information("Emails has been sent");
                        return;
                    }
                    catch (Exception ex)
                    {
                        lastFailure = GetSafeExceptionType(ex);
                        Log.Logger.Warning(
                            "Email sending failed with configured sender EmailId={EmailId}. Trying next configured sender. ErrorType={ErrorType}",
                            GetLogSafeEmailIdentifier(userEmail),
                            lastFailure);
                    }
                }

                throw new InvalidOperationException($"Unable to send email with available sender settings. LastErrorType={lastFailure}");
            }
            catch (Exception ex)
            {
                Log.Logger.Error("Error while sending email. ErrorType={ErrorType}", GetSafeExceptionType(ex));
                throw new EmailSenderException("Error while sending email.", ex);
            }
        }

        public async Task<bool> TrySendQueuedEmail(string queuedPayload)
        {
            if (!TryParseQueuedEmailPayload(queuedPayload, out var payload))
                return false;

            if (!IsValidQueuedEmailPayload(payload))
                return false;

            MimeMessage message;
            try
            {
                using var stream = new MemoryStream(Encoding.UTF8.GetBytes(payload.RawMimeMessage));
                message = MimeMessage.Load(stream);
            }
            catch
            {
                return false;
            }

            await SendEmail(message, payload.EnterpriseId);
            return true;
        }

        private async Task<IEnumerable<Email>> ResolveEmailSettingsCandidatesAsync(Guid? enterpriseId = null)
        {
            var storedSettings = (await _emailRepository.GetAllAsync(x =>
                    x.IsActive != false &&
                    (!enterpriseId.HasValue || x.EnterpriseId == enterpriseId.Value || x.EnterpriseId == null)))
                ?.OrderByDescending(x => enterpriseId.HasValue && x.EnterpriseId == enterpriseId.Value)
                .ThenByDescending(x => x.CreatedAt)
                .ToList() ?? [];

            var candidates = new List<Email>(storedSettings);

            var configuredSettings = _configuration.GetSection("EmailSettings");
            var hostName = ResolveConfigValue(configuredSettings["HostName"]);
            var userName = ResolveConfigValue(configuredSettings["Username"]);
            var password = ResolveConfigValue(configuredSettings["Password"]);
            var portRaw = ResolveConfigValue(configuredSettings["Port"]);

            if (!string.IsNullOrWhiteSpace(hostName) && !string.IsNullOrWhiteSpace(userName))
            {
                _ = int.TryParse(portRaw, out var port);
                var configuredCandidate = new Email
                {
                    HostName = hostName,
                    UserName = userName,
                    Password = password,
                    Port = port == 0 ? 587 : port,
                    IsActive = true
                };

                var hasSameStoredSetting = candidates.Any(x =>
                    string.Equals(x.HostName?.Trim(), configuredCandidate.HostName?.Trim(), StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(x.UserName?.Trim(), configuredCandidate.UserName?.Trim(), StringComparison.OrdinalIgnoreCase));

                if (!hasSameStoredSetting)
                    candidates.Add(configuredCandidate);
            }

            return candidates;
        }

        private async Task<bool> TryAuthenticateWithGoogleOAuthAsync(MailKit.Net.Smtp.SmtpClient smtpClient, string userEmail)
        {
            try
            {
                var clientId = ResolveConfigValue(_configuration.GetSection("GoogleSettings")["ClientId"]);
                var clientSecret = ResolveConfigValue(_configuration.GetSection("GoogleSettings")["ClientSecret"]);
                if (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(clientSecret))
                    return false;
                if (LooksLikeSecretReference(clientId) || LooksLikeSecretReference(clientSecret))
                    return false;

                var accessToken = await GetGmailAccessTokenAsync(clientId, clientSecret, userEmail);
                if (string.IsNullOrWhiteSpace(accessToken))
                    return false;

                var oauth2 = new SaslMechanismOAuth2(userEmail, accessToken);
                await smtpClient.AuthenticateAsync(oauth2);
                return true;
            }
            catch (Exception ex)
            {
                Log.Logger.Warning("Google OAuth authentication failed for email sender EmailId={EmailId}. Continuing with next configured sender. ErrorType={ErrorType}", GetLogSafeEmailIdentifier(userEmail), GetSafeExceptionType(ex));
                return false;
            }
        }

        private bool ShouldAttemptInteractiveGoogleOAuthFallback()
        {
            return ShouldAttemptInteractiveGoogleOAuthFallback(
                _configuration,
                OperatingSystem.IsWindows(),
                IsRunningInContainer());
        }

        private static bool ShouldAttemptInteractiveGoogleOAuthFallback(IConfiguration configuration, bool isWindows, bool isContainer)
        {
            if (configuration.GetValue<bool?>(EnableInteractiveGoogleOAuthFallbackConfigKey) != true)
                return false;

            return isWindows && !isContainer;
        }

        private static bool IsRunningInContainer()
        {
            return IsTruthyEnvironmentValue(Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER")) ||
                   IsTruthyEnvironmentValue(Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINERS")) ||
                   File.Exists("/.dockerenv");
        }

        private static bool IsTruthyEnvironmentValue(string? value)
        {
            return string.Equals(value, "true", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(value, "1", StringComparison.OrdinalIgnoreCase);
        }

        private static string GetLogSafeEmailIdentifier(string? email)
        {
            var normalizedEmail = (email ?? string.Empty).Trim().ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(normalizedEmail))
                return "empty";

            var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(normalizedEmail));
            return Convert.ToHexString(bytes).ToLowerInvariant()[..12];
        }

        private static string GetSafeExceptionType(Exception? exception)
        {
            return exception?.GetType().Name ?? "UnknownError";
        }

        private string CreateQueuedEmailPayload(Guid enterpriseId, string rawMimeMessage)
        {
            if (enterpriseId == Guid.Empty)
                throw new InvalidOperationException("Queued email enterprise is required.");

            if (string.IsNullOrWhiteSpace(rawMimeMessage))
                throw new InvalidOperationException("Queued email MIME payload is required.");

            var payload = new QueuedEmailPayload
            {
                EnterpriseId = enterpriseId,
                RawMimeMessage = rawMimeMessage
            };
            payload.Signature = CreateQueuedEmailSignature(payload.EnterpriseId, payload.RawMimeMessage);
            return JsonSerializer.Serialize(payload);
        }

        private static bool TryParseQueuedEmailPayload(string rawPayload, out QueuedEmailPayload payload)
        {
            payload = new QueuedEmailPayload();
            if (string.IsNullOrWhiteSpace(rawPayload))
                return false;

            try
            {
                var parsedPayload = JsonSerializer.Deserialize<QueuedEmailPayload>(
                    rawPayload,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                if (parsedPayload == null ||
                    parsedPayload.EnterpriseId == Guid.Empty ||
                    string.IsNullOrWhiteSpace(parsedPayload.RawMimeMessage) ||
                    string.IsNullOrWhiteSpace(parsedPayload.Signature))
                {
                    return false;
                }

                payload = parsedPayload;
                return true;
            }
            catch (JsonException)
            {
                return false;
            }
        }

        private bool IsValidQueuedEmailPayload(QueuedEmailPayload payload)
        {
            string expectedSignature;
            try
            {
                expectedSignature = CreateQueuedEmailSignature(payload.EnterpriseId, payload.RawMimeMessage);
            }
            catch
            {
                return false;
            }

            try
            {
                var expectedBytes = Convert.FromBase64String(expectedSignature);
                var providedBytes = Convert.FromBase64String(payload.Signature);
                return expectedBytes.Length == providedBytes.Length &&
                       CryptographicOperations.FixedTimeEquals(expectedBytes, providedBytes);
            }
            catch (FormatException)
            {
                return false;
            }
        }

        private string CreateQueuedEmailSignature(Guid enterpriseId, string rawMimeMessage)
        {
            var signingKey = ResolveQueueSigningKey();
            using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(signingKey));
            var data = Encoding.UTF8.GetBytes($"{enterpriseId:N}\n{rawMimeMessage}");
            return Convert.ToBase64String(hmac.ComputeHash(data));
        }

        private string ResolveQueueSigningKey()
        {
            var configuredKey = ResolveConfigValue(_configuration.GetSection("EmailQueue")["SigningKey"]);
            if (!string.IsNullOrWhiteSpace(configuredKey))
                return configuredKey;

            var encryptionKey = ResolveConfigValue(_configuration.GetSection("Encryption")["Key"]);
            if (!string.IsNullOrWhiteSpace(encryptionKey))
                return encryptionKey;

            throw new InvalidOperationException("Email queue signing key is not configured.");
        }

        private sealed class QueuedEmailPayload
        {
            public int Version { get; set; } = 1;
            public Guid EnterpriseId { get; set; }
            public string RawMimeMessage { get; set; } = string.Empty;
            public string Signature { get; set; } = string.Empty;
        }

        private string ResolveConfigValue(string? rawValue)
        {
            if (string.IsNullOrWhiteSpace(rawValue))
                return string.Empty;

            var value = rawValue.Trim();
            var mightBeSecretReference = value.StartsWith("plain:", StringComparison.OrdinalIgnoreCase) ||
                value.Contains('/') ||
                value.Contains(':');

            if (!mightBeSecretReference)
                return value;

            try
            {
                var resolvedValue = _kmsProvider.GetKMSKey(value);
                return string.IsNullOrWhiteSpace(resolvedValue) ? value : resolvedValue;
            }
            catch
            {
                return value;
            }
        }

        private static IEnumerable<string> ResolveEmailPasswordCandidates(string? rawPassword)
        {
            if (string.IsNullOrWhiteSpace(rawPassword))
                return Enumerable.Empty<string>();

            var candidates = new List<string>();
            var normalizedRaw = rawPassword.Trim();
            var decrypted = SharedFunctions.Decrypt(normalizedRaw);

            AddPasswordCandidate(candidates, decrypted);
            AddPasswordCandidate(candidates, normalizedRaw);

            return candidates.Where(candidate => !string.IsNullOrWhiteSpace(candidate));
        }

        private static void AddPasswordCandidate(List<string> candidates, string? rawCandidate)
        {
            if (string.IsNullOrWhiteSpace(rawCandidate))
                return;

            var candidate = rawCandidate.Trim();
            if (!candidates.Contains(candidate, StringComparer.Ordinal))
                candidates.Add(candidate);

            var withoutSpaces = candidate.Replace(" ", string.Empty);
            if (!string.Equals(withoutSpaces, candidate, StringComparison.Ordinal) &&
                !candidates.Contains(withoutSpaces, StringComparer.Ordinal))
            {
                candidates.Add(withoutSpaces);
            }
        }

        private static bool LooksLikeSecretReference(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return false;

            return value.StartsWith("plain:", StringComparison.OrdinalIgnoreCase) ||
                value.Contains('/') ||
                value.Contains(':');
        }

        private async Task ConnectToAllowedSmtpDestinationAsync(MailKit.Net.Smtp.SmtpClient smtpClient, string? hostName, int port)
        {
            if (!IsAllowedSmtpPort(port))
                throw new InvalidOperationException("Blocked SMTP destination.");

            var normalizedHost = (hostName ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(normalizedHost))
                throw new InvalidOperationException("Blocked SMTP destination.");

            if (normalizedHost.Equals("localhost", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Blocked SMTP destination.");

            if (_configuration.GetValue<bool?>(AllowPrivateSmtpDestinationsConfigKey) == true)
            {
                await smtpClient.ConnectAsync(normalizedHost, port, SecureSocketOptions.StartTls);
                return;
            }

            var addresses = await ResolveAllowedSmtpAddressesAsync(normalizedHost);
            if (addresses.Length == 0)
                throw new InvalidOperationException("Blocked SMTP destination.");

            var address = addresses[0];
            var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
            try
            {
                await socket.ConnectAsync(new IPEndPoint(address, port));
                await smtpClient.ConnectAsync(socket, normalizedHost, port, SecureSocketOptions.StartTls);
            }
            catch
            {
                socket.Dispose();
                throw;
            }
        }

        private static async Task<IPAddress[]> ResolveAllowedSmtpAddressesAsync(string normalizedHost)
        {
            IPAddress[] addresses;
            try
            {
                addresses = await Dns.GetHostAddressesAsync(normalizedHost);
            }
            catch
            {
                return [];
            }

            if (addresses == null || addresses.Length == 0)
                return [];

            if (addresses.Any(IsRestrictedAddress))
                return [];

            return addresses
                .OrderBy(address => address.AddressFamily == AddressFamily.InterNetwork ? 0 : 1)
                .ToArray();
        }

        private static bool IsAllowedSmtpPort(int port)
        {
            return port is 25 or 465 or 587;
        }

        private static bool IsRestrictedAddress(IPAddress address)
        {
            if (IPAddress.IsLoopback(address))
                return true;

            if (address.AddressFamily == AddressFamily.InterNetwork)
            {
                var bytes = address.GetAddressBytes();
                var first = bytes[0];
                var second = bytes[1];

                if (first == 10 || first == 127 || first == 0)
                    return true;

                if (first == 172 && second >= 16 && second <= 31)
                    return true;

                if (first == 192 && second == 168)
                    return true;

                if (first == 169 && second == 254)
                    return true;

                if (first >= 224)
                    return true;

                return false;
            }

            if (address.AddressFamily == AddressFamily.InterNetworkV6)
            {
                if (address.IsIPv6LinkLocal || address.IsIPv6Multicast || address.IsIPv6SiteLocal)
                    return true;

                var bytes = address.GetAddressBytes();
                var first = bytes[0];
                if ((first & 0xFE) == 0xFC)
                    return true;

                return false;
            }

            return true;
        }

        public async Task SendManualEmail(EmailStructure email, Enterprise enterprise)
        {
            try
            {
                if (enterprise == null || enterprise.Id == Guid.Empty)
                    throw new EmailSenderException("Email enterprise is required.");

                var enterpriseId = enterprise.Id;

                // Validate email
                if (email.RecipientEmails == null || email.RecipientEmails?.Any() == false || await ShouldSendEmail(email, enterprise) == false)
                {
                    Log.Logger.Warning($"Shouldn't send email.");
                    return;
                }

                var message = await CreateEmail(email, enterpriseId);

                var messageSummary = new
                {
                    Subject = message.Subject,
                    From = message.From.ToString(),
                    To = message.To.ToString(),
                    TextBody = message.TextBody,
                    HtmlBody = message.HtmlBody,
                    Date = message.Date
                };

                // Put in the Email Queue
                Log.Logger.Information("Sending email to the Queue");
                using (var stream = new MemoryStream())
                {
                    message.WriteTo(stream);
                    var rawMessage = Encoding.UTF8.GetString(stream.ToArray());
                    await _rabbitMQManager.EnqueueAsync<string>(CreateQueuedEmailPayload(enterpriseId, rawMessage));
                }
                Log.Logger.Information("Email has been enqueued");
            }
            catch (Exception ex)
            {
                Log.Logger.Error("Couldn't send manual email. ErrorType={ErrorType}", GetSafeExceptionType(ex));
                throw;
            }
        }

        #region Monthly Order Email
        public async Task SendMonthEmail(Guid? enterpriseId = null)
        {
            try
            {
                var enterprises = await _enterpriseRepository
                    .GetAllAsync(x =>
                        !string.IsNullOrEmpty(x.Email) &&
                        (enterpriseId.HasValue
                            ? x.Id == enterpriseId.Value
                            : x.ShouldSendMonthlyBilling)) ?? new List<Enterprise>();

                foreach (Enterprise enterprise in enterprises)
                {

                    var body = await _orderService.GetOrdersBodyAsync(enterprise);

                    if (!string.IsNullOrEmpty(body))
                    {
                        List<string> recipientEmails = new List<string>() { enterprise.Email };

                        var email = new EmailStructure(body.ToString(), "Monthly Email", recipientEmails);

                        // Validate email
                        if (email.RecipientEmails == null || email.RecipientEmails?.Any() == false || await ShouldSendEmail(email, enterprise) == false)
                        {
                            Log.Logger.Warning($"Shouldn't send email.");
                            return;
                        }

                        await SendManualEmail(email, enterprise);
                    }
                }
            }
            catch (EmailSenderException ex)
            {
                Log.Logger.Warning("Email sender failure during monthly email dispatch. ErrorType={ErrorType}", GetSafeExceptionType(ex));
            }
            catch (Exception ex)
            {
                Log.Logger.Error("Couldn't send monthly email. ErrorType={ErrorType}", GetSafeExceptionType(ex));
                throw;
            }
        }

        #endregion

        #region Order Status Email
        #endregion

        #region Stock Email 
        public async Task SendStockEmail(Guid? enterpriseId = null)
        {
            try
            {
                var enterprises = await _enterpriseRepository
                    .GetAllAsync(x =>
                        !string.IsNullOrEmpty(x.Email) &&
                        (!enterpriseId.HasValue || x.Id == enterpriseId.Value)) ?? new List<Enterprise>();

                foreach (Enterprise enterprise in enterprises)
                {
                    var body = await _productService.GetProductsBodyAsync(enterprise);

                    if (!string.IsNullOrEmpty(body))
                    {
                        List<string> recipientEmails = new List<string>() { enterprise.Email };

                        var email = new EmailStructure(body.ToString(), "Weekly Stock Email", recipientEmails);
                        await SendManualEmail(email, enterprise);
                    }
                }
            }
            catch (EmailSenderException ex)
            {
                Log.Logger.Warning("Email sender failure during stock email dispatch. ErrorType={ErrorType}", GetSafeExceptionType(ex));
            }
            catch (Exception ex)
            {
                Log.Logger.Error("Couldn't send stock email. ErrorType={ErrorType}", GetSafeExceptionType(ex));
                throw;
            }
        }
        #endregion

        private async Task<bool> ShouldSendEmail(EmailStructure email, Enterprise enterprise)
        {
            // Validate the Email

            if (string.IsNullOrEmpty(email.Body))
            {
                Log.Logger.Error($"Invalid Email Body.");
                return false;
            }

            // Validate recipients's email
            var validRecipients = new List<string>();

            foreach (var recipientEmail in email.RecipientEmails ?? Enumerable.Empty<string>())
            {
                var normalizedRecipient = (recipientEmail ?? string.Empty).Trim();

                if (!await SharedFunctions.IsEmailValid<string>(normalizedRecipient))
                {
                    Log.Logger.Error(
                        "Invalid recipient email. EmailId={EmailId}.",
                        GetLogSafeEmailIdentifier(normalizedRecipient));
                    continue;
                }

                validRecipients.Add(normalizedRecipient);
            }

            email.RecipientEmails = validRecipients;
            return validRecipients.Any();
        }

        private async Task<string> GetGmailAccessTokenAsync(string clientId, string clientSecret, string userEmail)
        {
            var secrets = new ClientSecrets
            {
                ClientId = clientId,       // OAuth Desktop App
                ClientSecret = clientSecret
            };

            var scopes = new[] { "https://mail.google.com/" };

            // Porta fixa + Edge InPrivate
            var receiver = new FixedPortEdgeReceiver(54373);

                var credential = await GoogleWebAuthorizationBroker.AuthorizeAsync(
                    secrets,
                    scopes,
                    userEmail,
                    CancellationToken.None,
                    new NullDataStore(),
                    receiver
                );

            return await credential.GetAccessTokenForRequestAsync();
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
                    // Dispose managed resources here.
                    (_emailRepository as IDisposable)?.Dispose();
                    (_enterpriseRepository as IDisposable)?.Dispose();
                    (_orderService as IDisposable)?.Dispose();
                    (_rabbitMQManager as IDisposable)?.Dispose();
                }

                // Dispose unmanaged resources here.
                // For example:
                // Close file handles, release COM objects.

                disposed = true;
            }
        }

        // Destructor for finalization code
        ~EmailService()
        {
            Dispose(false);
        }

        #endregion
    }
}
