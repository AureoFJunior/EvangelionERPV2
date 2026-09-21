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

                    if (document.TransmittedAtUtc != null)
                        continue;

                    if (string.IsNullOrWhiteSpace(document.XmlContent))
                    {
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
