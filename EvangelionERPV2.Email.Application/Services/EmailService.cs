using EvangelionERPV2.EmailModule.Application.Interface;
using EvangelionERPV2.EnterpriseModule.Domain.Interface;
using EvangelionERPV2.OrderModule.Application.Interface;
using EvangelionERPV2.Shared.Entities;
using EvangelionERPV2.Shared.Entities.RabbitMQ;
using EvangelionERPV2.Shared.Exceptions;
using EvangelionERPV2.Shared.Interfaces;
using EvangelionERPV2.Shared.Utils;
using Microsoft.Extensions.Options;
using MimeKit;
using MimeKit.Text;
using Serilog;
using System.Net.Mail;

namespace EvangelionERPV2.EmailModule.Application.Services
{
    public class EmailService : IEmailService<Email>
    {
        private readonly IRepository<Enterprise> _enterpriseRepository;
        public readonly EmailSettings _emailSettings;
        public readonly IEmailRabbitMQManager _rabbitMQManager;
        public readonly IOrderService<Order> _orderService;

        private bool disposed;

        public EmailService(IOptions<EmailSettings> emailSettings,
            IEmailRabbitMQManager rabbitMQManager,
            IRepository<Enterprise> enterpriseRepository,
            IOrderService<Order> orderService)
        {
            _emailSettings = emailSettings.Value;
            _rabbitMQManager = rabbitMQManager;
            _enterpriseRepository = enterpriseRepository;
            _orderService = orderService;
        }

        public async Task<MimeMessage> CreateEmail(Email email)
        {
            try
            {
                // Create the Email object

                var message = new MimeMessage();
                message.From.Add(new MailboxAddress(_emailSettings.Username, _emailSettings.HostName));

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
                // Send Email
                using var smtpClient = new SmtpClient(_emailSettings.HostName, _emailSettings.Port);
                smtpClient.EnableSsl = true;
                smtpClient.Credentials = new System.Net.NetworkCredential(_emailSettings.Username, _emailSettings.Password);

                Log.Logger.Information("Sending emails");
                smtpClient.Send(message.From.ToString(),
                    string.Join(',', message.GetRecipients(true).Select(x => x.Address.ToString())),
                    message.Subject,
                    message.Body.ToString());
                Log.Logger.Information("Emails has been sent");
            }
            catch (Exception ex)
            {
                Log.Logger.Error($"Error while sending email: {ex.Message}", ex.Message);
            }
        }

        public async Task SendManualEmail(Email email, Enterprise enterprise)
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

                // Put in the Email Queue
                Log.Logger.Information("Sending email to the Queue");
                await _rabbitMQManager.EnqueueAsync<MimeMessage>(message);
                Log.Logger.Information("Email has been enqueued");
            }
            catch (Exception ex)
            {
                Log.Logger.Error($"Couldn't send email.", ex.Message);
                throw;
            }
        }

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

                        var email = new Email(body.ToString(), "Monthly Email", recipientEmails);
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

        private async Task<bool> ShouldSendEmail(Email email, Enterprise enterprise)
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
                    (_emailSettings as IDisposable)?.Dispose();
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