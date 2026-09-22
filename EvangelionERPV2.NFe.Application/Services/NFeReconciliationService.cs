using EvangelionERPV2.NFeModule.Application.Configs;
using EvangelionERPV2.NFeModule.Application.Interface;
using EvangelionERPV2.NFeModule.Domain.Interface;
using EvangelionERPV2.NFeModule.Infra.Providers;
using EvangelionERPV2.Shared.Entities;
using Microsoft.Extensions.Options;
using Serilog;

namespace EvangelionERPV2.NFeModule.Application.Services
{
    /// <summary>
    /// Owner of every document the issue path could not finish. Two states reach it and they must
    /// never be confused:
    /// <list type="bullet">
    /// <item>transmitted, outcome unknown — ask SEFAZ and adopt the answer, never retransmit;</item>
    /// <item>never transmitted — provably nothing left this process, so the document goes back to
    /// the ordinary retry path, which reuses the number it already allocated.</item>
    /// </list>
    /// </summary>
    public sealed class NFeReconciliationService
    {
        private readonly EvangelionERPV2.Shared.Repositories.IRepository<NFeDocument> _nfeRepository;
        private readonly INFeRepository<NFeDocument> _nfeRepositoryCustom;
        private readonly INFeProvider _provider;
        private readonly INFeFiscalSettingsService _fiscalSettingsService;
        private readonly NFeSettings _settings;

        public NFeReconciliationService(
            EvangelionERPV2.Shared.Repositories.IRepository<NFeDocument> nfeRepository,
            INFeRepository<NFeDocument> nfeRepositoryCustom,
            INFeProvider provider,
            INFeFiscalSettingsService fiscalSettingsService,
            IOptions<NFeSettings> settings)
        {
            _nfeRepository = nfeRepository;
            _nfeRepositoryCustom = nfeRepositoryCustom;
            _provider = provider;
            _fiscalSettingsService = fiscalSettingsService;
            _settings = settings?.Value ?? new NFeSettings();
        }

        /// <summary>Runs one sweep.</summary>
        /// <returns>How many documents left the Pending state.</returns>
        public async Task<int> ReconcileAsync(CancellationToken cancellationToken = default)
        {
            if (!_settings.Enabled)
                return 0;

            var stale = await _nfeRepositoryCustom.GetStalePendingAsync(
                TimeSpan.FromMinutes(Math.Max(1, _settings.ReconciliationStaleAfterMinutes)),
                _settings.ReconciliationMaxAttempts);

            var resolved = 0;
            foreach (var document in stale)
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    if (document.IsOutcomeUnknown)
                    {
                        if (await ResolveUnknownOutcomeAsync(document))
                            resolved++;
                    }
                    else
                    {
                        await ReleaseNeverTransmittedAsync(document);
                        resolved++;
                    }
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    Log.Logger.Error(
                        exception,
                        "NFe reconciliation failed for document {DocumentId}.",
                        document.Id);
                }
            }

            return resolved;
        }

        /// <summary>
        /// Asks SEFAZ for the outcome of a note that was transmitted and never answered for.
        /// </summary>
        private async Task<bool> ResolveUnknownOutcomeAsync(NFeDocument document)
        {
            document.AttemptCount++;
            document.UpdatedAt = DateTime.UtcNow;

            var resolved = false;
            if (string.IsNullOrWhiteSpace(document.AccessKey))
            {
                Log.Logger.Error(
                    "NFe document {DocumentId} has an unknown outcome and no access key; it cannot be consulted.",
                    document.Id);
            }
            else
            {
                var fiscalSettings = await _fiscalSettingsService.GetSettingsAsync(document.EnterpriseId);
                var result = await _provider.ConsultAsync(document.AccessKey, fiscalSettings);

                if (result.Status == NFeStatus.Pending)
                {
                    Log.Logger.Warning(
                        "NFe document {DocumentId} is still unresolved at SEFAZ after {AttemptCount} attempts.",
                        document.Id,
                        document.AttemptCount);
                }
                else
                {
                    NFeService.ApplyOutcome(document, result);

                    Log.Logger.Information(
                        "NFe document {DocumentId} reconciled to {Status} (cStat {Cstat}).",
                        document.Id,
                        document.Status,
                        document.Cstat);

                    resolved = true;
                }
            }

            _nfeRepository.Update(document);
            await _nfeRepository.CommitAsync();

            return resolved;
        }

        /// <summary>
        /// Hands a document that provably never left back to the ordinary retry path.
        /// </summary>
        private async Task ReleaseNeverTransmittedAsync(NFeDocument document)
        {
            document.Status = NFeStatus.Error;
            document.AttemptCount++;
            document.UpdatedAt = DateTime.UtcNow;

            _nfeRepository.Update(document);
            await _nfeRepository.CommitAsync();

            Log.Logger.Warning(
                "NFe document {DocumentId} was claimed but never transmitted; released for retry with number {Number}.",
                document.Id,
                document.Number);
        }
    }
}
