using Microsoft.EntityFrameworkCore;

namespace EvangelionERPV2.Shared.Entities
{
    [Index(nameof(CreatedAt), nameof(UpdatedAt), nameof(IsActive), nameof(UserName))]
    [Index(nameof(UserName))]
    public class Email : BaseEntity
    {
        public Email() { }

        public Email(string hostname, string username, string password, int port)
        {
           HostName = hostname;
           UserName = username;
           Password = password;
           Port = port;
        }

        public string HostName { get; set; }
        public string UserName { get; set; }
        public string Password { get; set; }
        public int Port { get; set; }
    }
}