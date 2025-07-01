using EvangelionERPV2.EmailModule.Application.Interface;
using EvangelionERPV2.EnterpriseModule.Domain.Interface;
using EvangelionERPV2.OrderModule.Application.Interface;
using EvangelionERPV2.Shared.Entities;
using EvangelionERPV2.Shared.Exceptions;
using EvangelionERPV2.Shared.Utils;
using MimeKit;
using MimeKit.Text;
using Serilog;
using System.Net.Mail;

namespace EvangelionERPV2.EmailModule.Application.Services
{
    public class EmailService : IEmailService<EmailStructure>
    {
        private readonly IRepository<Enterprise> _enterpriseRepository;
        private readonly Domain.Interface.IRepository<Email> _emailRepository;
        public readonly IEmailRabbitMQManager _rabbitMQManager;
        public readonly IOrderService<Order> _orderService;

        private bool disposed;

        public EmailService(
            IEmailRabbitMQManager rabbitMQManager,
            IRepository<Enterprise> enterpriseRepository,
            IOrderService<Order> orderService,
            Domain.Interface.IRepository<Email> emailRepository)
        {
            _rabbitMQManager = rabbitMQManager;
            _enterpriseRepository = enterpriseRepository;
            _orderService = orderService;
            _emailRepository = emailRepository;
        }

        public async Task<Email> CreateAsync(Email email)
        {
            try
            {
                var existentEmail = _enterpriseRepository.GetById(email.Id);
                Email includedEmail= new Email();

                if (existentEmail!= null)
                    throw new InsertDatabaseException($"{nameof(Email)} already has an register in database");

                includedEmail= await _emailRepository.CreateAsync(email);
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
                message.From.Add(new MailboxAddress(emailSettings?.UserName, emailSettings?.HostName));

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

                // Send Email
                using var smtpClient = new SmtpClient(emailSettings?.HostName, emailSettings?.Port ?? 0);
                smtpClient.EnableSsl = true;
                smtpClient.Credentials = new System.Net.NetworkCredential(emailSettings?.UserName, emailSettings?.Password);

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