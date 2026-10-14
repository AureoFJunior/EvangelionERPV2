using System;

namespace EvangelionERPV2.Shared.Entities
{
    public class EmailStructure
    {
        public EmailStructure() { }

        public EmailStructure(string body, string subject, IEnumerable<string> recipientEmails)
        {
            Body = body ?? string.Empty;
            Subject = subject ?? string.Empty;
            RecipientEmails = recipientEmails ?? Array.Empty<string>();
        }

        public string Body { get; set; } = string.Empty;
        public string Subject { get; set; } = string.Empty;
        public IEnumerable<string> RecipientEmails { get; set; } = Array.Empty<string>();
    }
}
