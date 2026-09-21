namespace EvangelionERPV2.Shared.DTOs
{
    /// <summary>
    /// The NFC-e Código de Segurança do Contribuinte and the id it pairs with.
    /// </summary>
    public sealed class UpdateNFCeCscRequestDTO
    {
        /// <summary>idCSC / CSCIDToken, issued with the CSC by the UF's SEFAZ portal.</summary>
        public int CscId { get; set; }

        /// <summary>The CSC itself. Never logged, never echoed.</summary>
        public string Csc { get; set; } = string.Empty;
    }
}
