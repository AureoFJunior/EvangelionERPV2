using Microsoft.Extensions.Configuration;
using System;

namespace EvangelionERPV2.Domain.Models.RabbitMQ
{
    public class RabbitMQSettings
    {
        public RabbitMQSettings(string hostName, string userName, string password, string virtualHost, int port, string uri)
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
            HostName = configurationSection["HostName"];
            UserName = configurationSection["UserName"];
            Password = configurationSection["Password"];
            VirtualHost = configurationSection["VirtualHost"];
            Port = Convert.ToInt32(configurationSection["Port"]);
            Uri = configurationSection["Uri"];
        }

        public RabbitMQSettings(){}

        public string HostName { get; set; }
        public string UserName { get; set; }
        public string Password { get; set; }
        public string VirtualHost { get; set; }
        public int Port { get; set; }
        public string Uri { get; set; }

    }
}
