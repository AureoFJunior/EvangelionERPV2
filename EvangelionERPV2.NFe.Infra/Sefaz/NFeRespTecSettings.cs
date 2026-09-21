namespace EvangelionERPV2.NFeModule.Infra.Sefaz
{
    /// <summary>
    /// The <c>infRespTec</c> group: who wrote the software that issued the nota.
    /// </summary>
    public class NFeRespTecSettings
    {
        /// <summary>CNPJ of the software house, digits only.</summary>
        public string Cnpj { get; set; } = string.Empty;

        /// <summary>xContato — the person at the software house SEFAZ may call.</summary>
        public string ContactName { get; set; } = string.Empty;

        public string Email { get; set; } = string.Empty;

        /// <summary>DDD + number, digits only.</summary>
        public string Phone { get; set; } = string.Empty;

        /// <summary>
        /// idCSRT and the CSRT itself, required only where the UF demands them (chiefly NFC-e).
        /// </summary>
        public string IdCsrt { get; set; } = string.Empty;

        public string CsrtSecret { get; set; } = string.Empty;
    }

    /// <summary>The resolved group, ready to be written onto a nota.</summary>
    public sealed record NFeResponsibleTechnician(
        string Cnpj,
        string ContactName,
        string Email,
        string Phone,
        string? IdCsrt,
        string? Csrt);
}
