using System;
using System.Collections.Generic;
using System.Text;

namespace EvangelionERPV2.Shared.DTOs
{
    public sealed class GoogleCodeExchangeRequest
    {
        public string Code { get; set; } = string.Empty;
        public string RedirectUri { get; set; } = string.Empty;
        public string CodeVerifier { get; set; } = string.Empty;
    }
}
