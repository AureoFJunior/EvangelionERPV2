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

namespace EvangelionERPV2.EmailModule.Application.Services
{
    public class EmailService : IEmailService<EmailStructure>
    {
        private readonly IRepository<Enterprise> _enterpriseRepository;
        private readonly Domain.Interface.IRepository<Email> _emailRepository;
        public readonly IEmailRabbitMQManager _rabbitMQManager;
        public readonly IOrderService<Order> _orderService;
        private readonly AWSKMSKeyProvider _kmsProvider;
        private readonly IConfiguration _configuration;
        private bool disposed;

        public EmailService(
            IEmailRabbitMQManager rabbitMQManager,
            IRepository<Enterprise> enterpriseRepository,
            IOrderService<Order> orderService,
            Domain.Interface.IRepository<Email> emailRepository,
            AWSKMSKeyProvider kmsProvider,
            IConfiguration configuration)
        {
            _rabbitMQManager = rabbitMQManager;
            _enterpriseRepository = enterpriseRepository;
            _orderService = orderService;
            _emailRepository = emailRepository;
            _kmsProvider = kmsProvider;
            _configuration = configuration;
        }

        public async Task<Email> CreateAsync(Email email)
        {
            try
            {
                email.Password = SharedFunctions.Encrypt(email.Password);

                var existentEmail = _enterpriseRepository.GetById(email.Id);
                Email includedEmail= new Email();

                if (existentEmail!= null)
                    throw new InsertDatabaseException($"{nameof(Email)} already has an register in database");

                
                includedEmail = await _emailRepository.CreateAsync(email);
                await _emailRepository.CommitAsync();
                return includedEmail;

            }
            catch (Exception ex)
            {
                throw new InsertDatabaseException(ex.Message, ex.InnerException);
            }
        }

        public async Task<MimeMessage> CreateEmail(EmailStructure email)
        {
            try
            {
                // Create the Email object

                var message = new MimeMessage();
                IEnumerable<Email> emailsSettings = await _emailRepository.GetAllAsync();
                Email emailSettings = emailsSettings?.FirstOrDefault();
                message.From.Add(new MailboxAddress(emailSettings?.UserName, emailSettings?.UserName));

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
                Log.Logger.Error($"Error while creating email: {ex.Message}", ex.Message);
                throw new EmailSenderException();
            }
        }

        public async Task SendEmail(MimeMessage message)
        {
            try
            {
                IEnumerable<Email> emailsSettings = await _emailRepository.GetAllAsync();
                Email emailSettings = emailsSettings?.FirstOrDefault();

                // Get the access token (cache and refresh as needed in production)
                string clientId = _kmsProvider.GetKMSKey(_configuration.GetSection("GoogleSettings")["ClientId"]);
                string clientSecret = _kmsProvider.GetKMSKey(_configuration.GetSection("GoogleSettings")["ClientSecret"]);
                string userEmail = emailSettings?.UserName;
                string accessToken = await GetGmailAccessTokenAsync(clientId, clientSecret, userEmail);

                using var smtpClient = new MailKit.Net.Smtp.SmtpClient();
                await smtpClient.ConnectAsync(emailSettings?.HostName, emailSettings?.Port ?? 587, SecureSocketOptions.StartTls);

                // Authenticate using OAuth2
                var oauth2 = new SaslMechanismOAuth2(userEmail, accessToken);
                await smtpClient.AuthenticateAsync(oauth2);

                Log.Logger.Information("Sending emails");

                await smtpClient.SendAsync(message);
                await smtpClient.DisconnectAsync(true);

                Log.Logger.Information("Emails has been sent");
            }
            catch (Exception ex)
            {
                Log.Logger.Error($"Error while sending email: {ex.Message}", ex.Message);
            }
        }

        public async Task SendManualEmail(EmailStructure email, Enterprise enterprise)
        {
            try
            {
                // Validate email
                if (email.RecipientEmails?.Any() == false || await ShouldSendEmail(email, enterprise) == false)
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
                Log.Logger.Error($"Couldn't send email.", ex.Message);
                throw;
            }
        }

        #region Monthly Order Email
        public async Task SendMonthEmail(Guid? enterpriseId = null)
        {
            try
            {
                var enterprises = await _enterpriseRepository
                    .GetAllAsync(x => (x.Id == (enterpriseId ?? Guid.NewGuid()) || x.ShouldSendMonthlyBilling) && !string.IsNullOrEmpty(x.Email)) ?? new List<Enterprise>();

                foreach (Enterprise enterprise in enterprises)
                {
                    var body = await _orderService.GetOrdersBodyAsync(enterprise);

                    if (!string.IsNullOrEmpty(body))
                    {
                        List<string> recipientEmails = new List<string>() { enterprise.Email };

                        var email = new EmailStructure(body.ToString(), "Monthly Email", recipientEmails);
                        await SendManualEmail(email, enterprise);
                    }
                }
            }
            catch (EmailSenderException ex)
            {
                Log.Logger.Warning(ex.Message);
            }
            catch (Exception ex)
            {
                Log.Logger.Error($"Couldn't send email.", ex.Message);
                throw;
            }
        }

        #endregion

        #region Order Status Email
        #endregion

        #region Stock Email
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
            foreach (var recipientEmail in email.RecipientEmails)
            {

                if (await SharedFunctions.IsEmailValid<string>(recipientEmail) == false)
                {
                    Log.Logger.Error($"Invalid Email {recipientEmail}.");
                    email.RecipientEmails.ToList().Remove(recipientEmail);
                }
            }

            return email.RecipientEmails.Any();
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