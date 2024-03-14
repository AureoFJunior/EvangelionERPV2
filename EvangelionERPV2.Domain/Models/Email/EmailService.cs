using EvangelionERPV2.Domain.Exceptions;
using EvangelionERPV2.Domain.Interfaces;
using EvangelionERPV2.Domain.Interfaces.Repositories;
using EvangelionERPV2.Domain.Interfaces.Services;
using EvangelionERPV2.Domain.Models.RabbitMQ;
using EvangelionERPV2.Domain.Utils;
using Microsoft.Extensions.Options;
using MimeKit;
using MimeKit.Text;
using Serilog;
using System.Net.Mail;
using System.Text;

namespace EvangelionERPV2.Domain.Models
{
    public class EmailService : IEmailService<Email>
    {
        private readonly IRepository<Email> _emailRepository;
        private readonly IRepository<Enterprise> _enterpriseRepository;
        public readonly EmailSettings _emailSettings;
        public readonly IRabbitMQManager _rabbitMQManager;
        public readonly IOptions<EmailChannelSettings> _baseChannelSettings;

        public EmailService(IRepository<Email> emailRepository,
            IOptions<EmailSettings> emailSettings,
            IRabbitMQManager rabbitMQManager,
            IOptions<EmailChannelSettings> baseChannelSettings,
            IRepository<Enterprise> enterpriseRepository)
        {
            _emailRepository = emailRepository;
            _emailSettings = emailSettings.Value;
            _rabbitMQManager = rabbitMQManager;
            _baseChannelSettings = baseChannelSettings;
            _enterpriseRepository = enterpriseRepository;
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
                if (email.RecipientEmails?.Any() == false || await this.ShouldSendEmail(email, enterprise) == false)
                {
                    Log.Logger.Warning($"Shouldn't send email.");
                    return;
                }

                var message = await CreateEmail(email);

                // Put in the Email Queue
                Log.Logger.Information("Sending email to the Queue");
                await _rabbitMQManager.EnqueueAsync<MimeMessage>(message, _baseChannelSettings.Value);
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
                var enterprises = _enterpriseRepository
                    .GetByCondition(x => x.Id == (enterpriseId ?? Guid.NewGuid()) || x.ShouldSendMonthlyBilling).ToList() ?? new List<Enterprise>();

                foreach (Enterprise enterprise in enterprises)
                {
                    // TODO Create the logic and rules to fill the email
                    var body = new StringBuilder();

                    // TODO Create the body based on the Enterprise and the Billing Info

                    List<string> recipientEmails = new List<string>() { enterprise.Email };

                    var email = new Email(body.ToString(), "Monthly Email", recipientEmails);
                    await this.SendManualEmail(email, enterprise);
                }
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
    }
}