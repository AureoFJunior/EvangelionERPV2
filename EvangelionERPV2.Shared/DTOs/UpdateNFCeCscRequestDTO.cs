namespace EvangelionERPV2.Shared.DTOs
{
    /// <summary>
    /// The NFC-e Código de Segurança do Contribuinte and the id it pairs with.
    /// </summary>
    /// <remarks>
    /// Request-only. There is deliberately no response DTO carrying the CSC back: it is a signing
    /// key for the consumer QR, so it is written once and never read out again — the same posture
    /// as the certificate password.
    /// </remarks>
    public sealed class UpdateNFCeCscRequestDTO
    {
        /// <summary>idCSC / CSCIDToken, issued with the CSC by the UF's SEFAZ portal.</summary>
        public int CscId { get; set; }

        /// <summary>The CSC itself. Never logged, never echoed.</summary>
        public string Csc { get; set; } = string.Empty;
    }
}
