namespace EvangelionERPV2.Shared.Entities
{
    public class EmailStructure
    {
        public EmailStructure() { }

        public EmailStructure(string body, string subject, IEnumerable<string> recipientEmails)
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