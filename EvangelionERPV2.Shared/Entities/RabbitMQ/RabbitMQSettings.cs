using EvangelionERPV2.Shared.Utils;
using Microsoft.Extensions.Configuration;
using System;

namespace EvangelionERPV2.Shared.Entities.RabbitMQ
{
    public class RabbitMQSettings
    {
        public RabbitMQSettings(string hostName, string userName, string password, string virtualHost, string port, string uri)
        {
            HostName = hostName;
            UserName = userName;
            Password = password;
            VirtualHost = virtualHost;
            Port = port;
            Uri = uri;
        }

        public RabbitMQSettings(IConfigurationSection configurationSection)
        {
            HostName = configurationSection["HostName"] ?? string.Empty;
            UserName = configurationSection["UserName"] ?? string.Empty;
            Password = configurationSection["Password"] ?? string.Empty;
            VirtualHost = configurationSection["VirtualHost"] ?? string.Empty;
            Port = configurationSection["Port"] ?? string.Empty;
            Uri = configurationSection["Uri"] ?? string.Empty;
        }

        public RabbitMQSettings() { }

        public string HostName { get; set; } = string.Empty;
        public string UserName { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
        public string VirtualHost { get; set; } = string.Empty;
        public string Port { get; set; } = string.Empty;
        public string Uri { get; set; } = string.Empty;

    }
}
