using Microsoft.Extensions.Configuration;
using System;

namespace EvangelionERPV2.Shared.Entities
{
    public class BaseEmailSettings
    {
        public BaseEmailSettings() { }

        public string HostName { get; set; }
        public int Port { get; set; }
        public string Username { get; set; }
        public string Password { get; set; }
    }
}
