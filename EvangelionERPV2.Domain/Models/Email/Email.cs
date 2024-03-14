
namespace EvangelionERPV2.Domain.Models
{
    public class Email
    {
        public Email() { }

        public Email(string body, string subject, IEnumerable<string> recipientEmails)
        {
            Body = body;
            Subject = subject;
            RecipientEmails = recipientEmails;
        }

        public string Body { get; set; }
        public string Subject { get; set; }
        public IEnumerable<string> RecipientEmails { get; set; }
    }
}