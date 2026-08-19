using EvangelionERPV2.NFeModule.Application.Configs;
using EvangelionERPV2.NFeModule.Application.Providers;
using EvangelionERPV2.NFeModule.Domain.Interface;
using EvangelionERPV2.Shared.Entities;
using Microsoft.Extensions.Options;
using Serilog;

namespace EvangelionERPV2.NFeModule.Application.Services
{
    /// <summary>
    /// Owner of every document the issue path could not finish. Two states reach it and they must
    /// never be confused (docs/nfe/HANDOFF.md §4):
    /// <list type="bullet">
    /// <item>transmitted, outcome unknown — ask SEFAZ and adopt the answer, never retransmit;</item>
    /// <item>never transmitted — provably nothing left this process, so the document goes back to
    /// the ordinary retry path, which reuses the number it already allocated.</item>
    /// </list>
    /// </summary>
    /// <remarks>
    /// The write-ahead marker is what tells the two apart, so this class reads
    /// <see cref="NFeDocument.IsOutcomeUnknown"/> and never re-derives the condition.
    /// </remarks>
    public sealed class NFeReconciliationService
    {
        private readonly EvangelionERPV2.Shared.Repositories.IRepository<NFeDocument> _nfeRepository;
        private readonly INFeRepository<NFeDocument> _nfeRepositoryCustom;
        private readonly INFeProvider _provider;
        private readonly NFeSettings _settings;

        public NFeReconciliationService(
            EvangelionERPV2.Shared.Repositories.IRepository<NFeDocument> nfeRepository,
            INFeRepository<NFeDocument> nfeRepositoryCustom,
            INFeProvider provider,
            IOptions<NFeSettings> settings)
        {
            _nfeRepository = nfeRepository;
            _nfeRepositoryCustom = nfeRepositoryCustom;
            _provider = provider;
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
                    // One bad document must not stop the sweep: the others may be exactly the ones
                    // sitting on real tax liability.
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
        /// <remarks>
        /// The attempt counter is bumped and committed whatever happens, because it is the only
        /// brake on a document SEFAZ will never answer for: once it passes
        /// <see cref="NFeSettings.ReconciliationMaxAttempts"/> the query stops returning it and it
        /// waits for a human, instead of being consulted forever.
        /// </remarks>
        private async Task<bool> ResolveUnknownOutcomeAsync(NFeDocument document)
        {
            document.AttemptCount++;
            document.UpdatedAt = DateTime.UtcNow;

            var resolved = false;
            if (string.IsNullOrWhiteSpace(document.AccessKey))
            {
                // Unreachable through IssueAsync — the key is committed before the marker — but a
                // hand-edited row would otherwise be consulted with an empty key forever.
                Log.Logger.Error(
                    "NFe document {DocumentId} has an unknown outcome and no access key; it cannot be consulted.",
                    document.Id);
            }
            else
            {
                var result = await _provider.ConsultAsync(document.AccessKey, _settings);

                if (result.Status == NFeStatus.Pending)
                {
                    Log.Logger.Warning(
                        "NFe document {DocumentId} is still unresolved at SEFAZ after {AttemptCount} attempts.",
                        document.Id,
                        document.AttemptCount);
                }
                else
                {
                    document.Status = result.Status;
                    document.Cstat = result.Cstat;
                    document.XMotivo = result.XMotivo;

                    if (!string.IsNullOrWhiteSpace(result.Protocol))
                        document.Protocol = result.Protocol;

                    if (!string.IsNullOrWhiteSpace(result.XmlContent))
                        document.XmlContent = result.XmlContent;

                    if (result.IssuedAt.HasValue)
                    {
                        document.IssuedAt = result.IssuedAt;

                        // dhRecbto: the clock the cancellation window is measured from, so it is
                        // only meaningful for a note SEFAZ actually authorized.
                        if (result.Status == NFeStatus.Authorized)
                            document.AuthorizedAtUtc = result.IssuedAt;
                    }

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
        /// <remarks>
        /// Error is the retryable state, and the allocated number, series and chave are left intact:
        /// nothing was transmitted, so the same number may be sent again, and skipping it would need
        /// inutilização paperwork. <c>IssueAsync</c> reuses them when they are still current.
        /// </remarks>
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
