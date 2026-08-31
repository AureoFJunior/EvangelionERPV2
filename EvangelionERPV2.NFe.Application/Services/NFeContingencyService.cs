using EvangelionERPV2.NFeModule.Application.Configs;
using EvangelionERPV2.NFeModule.Application.Interface;
using EvangelionERPV2.NFeModule.Domain.Interface;
using EvangelionERPV2.NFeModule.Infra.Providers;
using EvangelionERPV2.Shared.Entities;
using EvangelionERPV2.Shared.Repositories;
using Microsoft.Extensions.Options;
using Serilog;

namespace EvangelionERPV2.NFeModule.Application.Services
{
    /// <summary>
    /// Transmits off-line NFC-e notes that were printed at a counter and still owe SEFAZ a copy.
    /// </summary>
    /// <remarks>
    /// The obligation is to transmit as soon as connectivity returns, so this sweeps often and
    /// sends whatever is outstanding rather than counting down to a deadline.
    ///
    /// **Nothing here ever gives up, and nothing here ever discards.** Past the configured deadline
    /// a note is not expired: it still has to reach SEFAZ, and it may then be rejected, which is a
    /// problem for a human rather than for a retry loop. So the deadline changes only how loudly
    /// this reports — never whether it keeps trying.
    ///
    /// It transmits the **stored** document and never rebuilds one. The note was signed and printed
    /// with a particular dhEmi and chave, and the customer is holding a receipt for that one.
    /// </remarks>
    public sealed class NFeContingencyService
    {
        private readonly IRepository<NFeDocument> _nfeRepository;
        private readonly INFeRepository<NFeDocument> _nfeRepositoryCustom;
        private readonly INFeProvider _provider;
        private readonly INFeFiscalSettingsService _fiscalSettingsService;
        private readonly NFeSettings _settings;
        private readonly TimeProvider _timeProvider;

        public NFeContingencyService(
            IRepository<NFeDocument> nfeRepository,
            INFeRepository<NFeDocument> nfeRepositoryCustom,
            INFeProvider provider,
            INFeFiscalSettingsService fiscalSettingsService,
            IOptions<NFeSettings> settings,
            TimeProvider? timeProvider = null)
        {
            _nfeRepository = nfeRepository;
            _nfeRepositoryCustom = nfeRepositoryCustom;
            _provider = provider;
            _fiscalSettingsService = fiscalSettingsService;
            _settings = settings?.Value ?? new NFeSettings();
            _timeProvider = timeProvider ?? TimeProvider.System;
        }

        /// <summary>Runs one sweep.</summary>
        /// <returns>How many notes reached SEFAZ on this pass.</returns>
        public async Task<int> TransmitPendingAsync(CancellationToken cancellationToken = default)
        {
            if (!_settings.Enabled)
                return 0;

            var outstanding = await _nfeRepositoryCustom.GetUntransmittedContingencyAsync();
            var transmitted = 0;

            foreach (var document in outstanding)
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    ReportAge(document);

                    // Provable non-transmission is the only safe licence to send. TransmittedAtUtc
                    // is the write-ahead marker and it is cleared only when a transport proves
                    // nothing left the process, so anything with it set may already exist at SEFAZ
                    // — including an Error raised after the socket opened, which IsOutcomeUnknown
                    // does not cover because that only describes Pending. Those belong to the
                    // reconciler, which consults instead of resending.
                    if (document.TransmittedAtUtc != null)
                        continue;

                    if (string.IsNullOrWhiteSpace(document.XmlContent))
                    {
                        // Nothing signed was kept, so there is nothing to send and nothing was
                        // printed either. Loud, because it is a sale with no fiscal document.
                        Log.Logger.Error(
                            "Contingency note {DocumentId} has no stored XML; it cannot be transmitted.",
                            document.Id);
                        continue;
                    }

                    var fiscalSettings = await _fiscalSettingsService.GetSettingsAsync(document.EnterpriseId);

                    var result = await _provider.TransmitSignedAsync(
                        document.AccessKey,
                        document.XmlContent,
                        fiscalSettings,
                        document.Type,
                        cancellationToken);

                    NFeService.ApplyOutcome(document, result);
                    document.AttemptCount++;

                    _nfeRepository.Update(document);
                    await _nfeRepository.CommitAsync(cancellationToken);

                    if (document.Status == NFeStatus.Authorized)
                        transmitted++;
                }
                catch (Exception exception)
                {
                    // One bad note must not stop the sweep: the others are sitting on real tax
                    // liability and a printed receipt in somebody's hand.
                    Log.Logger.Error(exception,
                        "Contingency transmission failed for document {DocumentId}.", document.Id);
                }
            }

            return transmitted;
        }

        /// <summary>
        /// Escalates by age. Three levels, all far below the legal bound, because the point is that
        /// a human acts long before it.
        /// </summary>
        private void ReportAge(NFeDocument document)
        {
            if (document.ContingencyEnteredAt is not { } enteredAt)
                return;

            var age = _timeProvider.GetUtcNow().UtcDateTime - enteredAt;

            if (age >= TimeSpan.FromHours(_settings.ContingencyDeadlineHours))
            {
                Log.Logger.Fatal(
                    "Contingency note {DocumentId} has been untransmitted for {Hours:0.0}h, past the {Deadline}h bound. It still owes SEFAZ a transmission.",
                    document.Id, age.TotalHours, _settings.ContingencyDeadlineHours);
            }
            else if (age >= TimeSpan.FromHours(_settings.ContingencyEscalateAfterHours))
            {
                Log.Logger.Error(
                    "Contingency note {DocumentId} has been untransmitted for {Hours:0.0}h.",
                    document.Id, age.TotalHours);
            }
            else if (age >= TimeSpan.FromHours(_settings.ContingencyWarnAfterHours))
            {
                Log.Logger.Warning(
                    "Contingency note {DocumentId} has been untransmitted for {Hours:0.0}h.",
                    document.Id, age.TotalHours);
            }
        }
    }
}
