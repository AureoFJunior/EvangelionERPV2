using EvangelionERPV2.Domain.Models.RabbitMQ;
using EvangelionERPV2.Domain.Utils;
using Microsoft.Extensions.Configuration;
using System;

namespace EvangelionERPV2.Domain.Models
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
            Port = SharedFunctions.SafeConvertToNumber<Int32>(configurationSection["Port"]);
        }

        public EmailSettings(){}
    }
}
