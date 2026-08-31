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

        #region Contingency

        /// <summary>How often untransmitted off-line notes are retried. Clamped to at least one minute.</summary>
        /// <remarks>
        /// Short on purpose. The obligation is to transmit as soon as connectivity returns, not to
        /// wait for a deadline, so this is a "send whatever is outstanding" cadence rather than a
        /// countdown.
        /// </remarks>
        public int ContingencyIntervalMinutes { get; set; } = 5;

        /// <summary>
        /// Hours after dhCont at which an untransmitted note is logged as a warning.
        /// </summary>
        /// <remarks>
        /// Deliberately far below <see cref="ContingencyDeadlineHours"/>. A first signal that fires
        /// at the legal limit is useless — a shop offline for a day needs a human long before then.
        /// </remarks>
        public int ContingencyWarnAfterHours { get; set; } = 1;

        /// <summary>Hours after dhCont at which it is logged as an error.</summary>
        public int ContingencyEscalateAfterHours { get; set; } = 4;

        /// <summary>
        /// The outer bound, in hours, after which an untransmitted note is a compliance breach.
        /// 24 is the national reference and is **unverified per UF**.
        /// </summary>
        /// <remarks>
        /// Configuration, not a constant, for the same reason as the consumer-identification
        /// threshold: a UF that disagrees must cost an edit rather than a release. Passing it
        /// changes nothing about what the sweeper does — the note still owes SEFAZ a transmission
        /// and is still retried forever. It only changes how loudly this is reported.
        /// </remarks>
        public int ContingencyDeadlineHours { get; set; } = 24;

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
