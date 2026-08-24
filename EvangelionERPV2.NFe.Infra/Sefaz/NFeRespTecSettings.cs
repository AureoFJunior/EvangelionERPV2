namespace EvangelionERPV2.NFeModule.Infra.Sefaz
{
    /// <summary>
    /// The <c>infRespTec</c> group: who wrote the software that issued the nota.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Installation-wide, not per tenant. NT 2018.005 asks for the CNPJ and contact of the
    /// <em>software developer</em>, which is the same company for every tenant this deployment
    /// serves — a column on <c>EnterpriseFiscalSettings</c> would ask each tenant to retype the
    /// same four values and let one of them get it wrong.
    /// </para>
    /// <para>
    /// Empty by default, and an incomplete block is omitted rather than half-filled: a placeholder
    /// CNPJ is a false declaration to the fisco. Filling these four values is the whole of
    /// <c>docs/fiscal/TODO-manual.md</c> §M13, and it is now configuration rather than a code change.
    /// </para>
    /// </remarks>
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
        /// <remarks>
        /// The CSRT is a shared secret and belongs in Secrets Manager — this value is resolved
        /// through <c>AWSKMSKeyProvider</c> like every other secret reference, so what sits in
        /// appsettings is the reference, never the code itself. The fiscal library derives
        /// <c>hashCSRT</c> from the CSRT and the chave on its own; nothing here computes it.
        /// </remarks>
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
