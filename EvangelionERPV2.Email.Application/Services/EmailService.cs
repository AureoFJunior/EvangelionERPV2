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

        public async Task<MimeMessage> CreateEmail(EmailStructure email)
        {
            try
            {
                // Create the Email object

                var message = new MimeMessage();
                var emailSettings = (await ResolveEmailSettingsCandidatesAsync()).FirstOrDefault();
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

        public async Task SendEmail(MimeMessage message)
        {
            try
            {
                var settingsCandidates = (await ResolveEmailSettingsCandidatesAsync()).ToList();
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
                        using var smtpClient = new MailKit.Net.Smtp.SmtpClient();
                        await smtpClient.ConnectAsync(emailSettings.HostName, emailSettings.Port == 0 ? 587 : emailSettings.Port, SecureSocketOptions.StartTls);

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
                            isAuthenticated = await TryAuthenticateWithGoogleOAuthAsync(smtpClient, userEmail);
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

        private async Task<IEnumerable<Email>> ResolveEmailSettingsCandidatesAsync()
        {
            var storedSettings = (await _emailRepository.GetAllAsync(x => x.IsActive != false))
                ?.OrderByDescending(x => x.CreatedAt)
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
                Log.Logger.Warning("Google OAuth authentication failed for email sender EmailId={EmailId}. Falling back to SMTP credentials. ErrorType={ErrorType}", GetLogSafeEmailIdentifier(userEmail), GetSafeExceptionType(ex));
                return false;
            }
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

        public async Task SendManualEmail(EmailStructure email, Enterprise enterprise)
        {
            try
            {
                // Validate email
                if (email.RecipientEmails == null || email.RecipientEmails?.Any() == false || await ShouldSendEmail(email, enterprise) == false)
                {
                    Log.Logger.Warning($"Shouldn't send email.");
                    return;
                }

                var message = await CreateEmail(email);

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
                    var rawMessage = System.Text.Encoding.UTF8.GetString(stream.ToArray());
                    await _rabbitMQManager.EnqueueAsync<string>(rawMessage);
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
                new FileDataStore("GmailOAuth2TokenStore", true),
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
