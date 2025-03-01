using EvangelionERPV2.Shared.Utils;
using Microsoft.Extensions.Configuration;

namespace EvangelionERPV2.Shared.Entities
{
    public class EmailSettings : BaseEmailSettings
    {
        public EmailSettings(string hostName, string userName, string password, int port)
        {
            HostName = hostName;
            Username = userName;
            Password = password;
            Port = port;
        }

        public EmailSettings(IConfigurationSection configurationSection)
        {
            HostName = configurationSection["HostName"];
            Username = configurationSection["UserName"];
            Password = configurationSection["Password"];
            Port = SharedFunctions.SafeConvertToNumber<int>(configurationSection["Port"]);
        }

        public EmailSettings() { }
    }
}
