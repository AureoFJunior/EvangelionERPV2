using EvangelionERPV2.Shared.Entities;

namespace EvangelionERPV2.NFeModule.Infra.Providers
{
    /// <summary>
    /// What a transport reports back about one SEFAZ exchange.
    /// </summary>
    public sealed class NFeProviderResult
    {
        /// <summary>Echoed back for correlation only. The caller allocated it and owns it.</summary>
        public string AccessKey { get; set; } = "";

        public string Protocol { get; set; } = "";
        public string XmlContent { get; set; } = "";
        public DateTime? IssuedAt { get; set; }
        public decimal TotalValue { get; set; }
        public NFeStatus Status { get; set; } = NFeStatus.Pending;

        /// <summary>cStat exactly as returned. Empty when no answer was obtained.</summary>
        public string Cstat { get; set; } = "";

        public string XMotivo { get; set; } = "";

        /// <summary>nRec from an asynchronous batch, needed to recover through RetAutorizacao.</summary>
        public string ReceiptNumber { get; set; } = "";

        /// <summary>
        /// True when the failure is worth retrying — a timeout, a 5xx, SEFAZ off the air. False for
        /// a rejection, which is a verdict rather than an accident.
        /// </summary>
        public bool IsTransient { get; set; }

        /// <summary>
        /// The consumer QR payload from <c>infNFeSupl/qrCode</c>. NFCe only — modelo 55 has none.
        /// </summary>
        public string QrCode { get; set; } = "";

        /// <summary>The UF's consulta-by-chave URL from <c>infNFeSupl/urlChave</c>. NFCe only.</summary>
        public string UrlChave { get; set; } = "";

        /// <summary>
        /// True only when the transport can prove nothing left this process — a certificate that
        /// would not load, a request rejected before the socket opened. It is the difference
        /// between "safe to send the same number again" and "must be consulted", so a transport
        /// that cannot prove it must leave this false.
        /// </summary>
        public bool NeverTransmitted { get; set; }
    }
}
