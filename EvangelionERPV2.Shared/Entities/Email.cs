using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations.Schema;

namespace EvangelionERPV2.Shared.Entities
{
    [Index(nameof(CreatedAt), nameof(UpdatedAt), nameof(IsActive), nameof(UserName))]
    [Index(nameof(UserName))]
    [Index(nameof(EnterpriseId), nameof(IsActive), nameof(UserName))]
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

        public string HostName { get; set; } = string.Empty;
        public string UserName { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
        public int Port { get; set; }

        [ForeignKey(nameof(Enterprise))]
        public Guid? EnterpriseId { get; set; }
        public Enterprise? Enterprise { get; set; }
    }
}
