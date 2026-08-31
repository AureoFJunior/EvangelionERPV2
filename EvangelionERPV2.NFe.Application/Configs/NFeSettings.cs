namespace EvangelionERPV2.NFeModule.Application.Configs
{
    public class NFeSettings
    {
        /// <summary>
        /// Global kill switch. Per-tenant issuance is controlled by
        /// EnterpriseFiscalSettings.Enabled; this AND-s over all tenants at once.
        /// </summary>
        public bool Enabled { get; set; } = false;

        public string Environment { get; set; } = "Homologation";
        public string StateCode { get; set; } = "RS";
        public string Series { get; set; } = "1";
        public int StartingNumber { get; set; } = 1;

        #region Reconciliation

        /// <summary>How often the sweeper runs. Clamped to at least one minute.</summary>
        public int ReconciliationIntervalMinutes { get; set; } = 5;

        /// <summary>
        /// How long a document may stay Pending before the reconciler takes it over. Must exceed the
        /// SEFAZ timeout comfortably, or the sweeper starts consulting notes still in flight.
        /// </summary>
        public int ReconciliationStaleAfterMinutes { get; set; } = 10;

        /// <summary>
        /// After this many sweeps a document stops being picked up and waits for a human. It is not
        /// resolved — an unknown outcome that SEFAZ will not answer is a fiscal problem, not a
        /// retry problem.
        /// </summary>
        public int ReconciliationMaxAttempts { get; set; } = 5;

        #endregion

        /// <summary>
        /// Secrets Manager prefix for per-tenant A1 certificates; the enterprise id is appended.
        /// Never accepts a "plain:" value — EnterpriseCertificateProvider rejects it in every
        /// environment, because a base64 PFX in configuration is a leaked signing identity.
        /// </summary>
        public string CertificateSecretPrefix { get; set; } = "evangelion/dev/nfe/cert/";

        /// <summary>
        /// Where each tenant's CSC lives. Same posture as the certificate prefix: the value behind
        /// it signs consumer QR codes, so EnterpriseCscProvider rejects a "plain:" prefix outright.
        /// </summary>
        public string CscSecretPrefix { get; set; } = "evangelion/dev/nfe/csc/";
    }
}
