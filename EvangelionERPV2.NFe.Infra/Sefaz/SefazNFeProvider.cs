using EvangelionERPV2.NFeModule.Infra.Certificates;
using EvangelionERPV2.NFeModule.Infra.Providers;
using EvangelionERPV2.NFeModule.Infra.Xml;
using EvangelionERPV2.Shared.Entities;
using EvangelionERPV2.Shared.Exceptions;
using EvangelionERPV2.Shared.Utils;
using Microsoft.Extensions.Options;

using Serilog;

using System.Globalization;
using System.Security.Cryptography.X509Certificates;
using System.Xml;

using Unimake.Business.DFe.Servicos;
using Unimake.Business.DFe.Utility;
using Unimake.Business.DFe.Xml.NFe;
using Unimake.Exceptions;

using NFeAutorizacao = Unimake.Business.DFe.Servicos.NFe.Autorizacao;
using NFeConsultaProtocolo = Unimake.Business.DFe.Servicos.NFe.ConsultaProtocolo;
using NFeRecepcaoEvento = Unimake.Business.DFe.Servicos.NFe.RecepcaoEvento;
using NFeStatusServico = Unimake.Business.DFe.Servicos.NFe.StatusServico;

namespace EvangelionERPV2.NFeModule.Infra.Sefaz
{
    /// <summary>
    /// The real SEFAZ transport. This is the only file in the solution that talks to an autorizador.
    /// </summary>
    public sealed class SefazNFeProvider : INFeProvider
    {
        /// <summary>Layout version of the cancelamento event; independent of the NFe schema version.</summary>
        private const string EventVersion = "1.00";

        private const string CancellationDescription = "Cancelamento";

        private const int MinimumJustificationLength = 15;
        private const int MaximumJustificationLength = 255;

        private readonly IEnterpriseCertificateProvider _certificates;
        private readonly IEnterpriseCscProvider? _cscProvider;
        private readonly NFeRespTecSettings _respTecSettings;
        private readonly AWSKMSKeyProvider? _kmsKeyProvider;

        public SefazNFeProvider(
            IEnterpriseCertificateProvider certificates,
            IOptions<NFeRespTecSettings>? respTecSettings = null,
            AWSKMSKeyProvider? kmsKeyProvider = null,
            IEnterpriseCscProvider? cscProvider = null)
        {
            _certificates = certificates;
            _respTecSettings = respTecSettings?.Value ?? new NFeRespTecSettings();
            _kmsKeyProvider = kmsKeyProvider;

            _cscProvider = cscProvider;
        }

        /// <summary>
        /// The configured <c>infRespTec</c>, or null when this installation has not been told who
        /// wrote the software.
        /// </summary>
        private async Task<NFeResponsibleTechnician?> ResolveResponsibleTechnicianAsync(CancellationToken cancellationToken)
        {
            var cnpj = OnlyDigits(_respTecSettings.Cnpj);
            var contact = _respTecSettings.ContactName?.Trim() ?? string.Empty;
            var email = _respTecSettings.Email?.Trim() ?? string.Empty;
            var phone = OnlyDigits(_respTecSettings.Phone);

            var provided = new[] { cnpj, contact, email, phone }.Count(x => !string.IsNullOrWhiteSpace(x));
            if (provided == 0)
                return null;

            if (provided < 4 || cnpj.Length != 14)
            {
                Log.Logger.Warning(
                    "infRespTec is configured but incomplete, so it was omitted from the note. See docs/fiscal/TODO-manual.md M13.");

                return null;
            }

            string? csrt = null;
            var idCsrt = _respTecSettings.IdCsrt?.Trim();

            if (!string.IsNullOrWhiteSpace(idCsrt) && !string.IsNullOrWhiteSpace(_respTecSettings.CsrtSecret) && _kmsKeyProvider != null)
            {
                csrt = await _kmsKeyProvider.GetKMSKeyAsync(_respTecSettings.CsrtSecret, cancellationToken);
            }

            return new NFeResponsibleTechnician(
                cnpj,
                contact,
                email,
                phone,
                string.IsNullOrWhiteSpace(csrt) ? null : idCsrt,
                string.IsNullOrWhiteSpace(csrt) ? null : csrt);
        }

        private static string OnlyDigits(string? value)
            => value == null ? string.Empty : new string(value.Where(char.IsDigit).ToArray());

        public async Task<NFeProviderResult> IssueAsync(NFeIssueContext context, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(context);

            var settings = context.FiscalSettings;
            NFeAutorizacao service;

            try
            {
                var certificate = await _certificates.GetAsync(settings.EnterpriseId, cancellationToken);

                var csc = context.Type == NFeDocumentType.NFCe && _cscProvider != null
                    ? await _cscProvider.GetAsync(settings.EnterpriseId, cancellationToken)
                    : null;

                var configuracao = UnimakeConfiguracaoFactory.Create(
                    settings, context.Type, certificate, csc, context.TipoEmissao);
                var enviNFe = NFeXmlBuilder.Build(context, await ResolveResponsibleTechnicianAsync(cancellationToken));

                service = new NFeAutorizacao(enviNFe, configuracao);
            }
            catch (Exception exception) when (IsPreTransmissionFailure(exception))
            {
                return PreTransmissionFailure(context.AccessKey, exception, "build");
            }

            var signedXml = Xml(service.ConteudoXMLAssinado);

            try
            {
                await Task.Run(service.Executar, cancellationToken);
            }
            catch (Exception exception)
            {
                Log.Logger.Error(exception, "NFe transmission failed for chave {AccessKey}.", Redact(context.AccessKey));

                var unknown = new NFeProviderResult
                {
                    AccessKey = context.AccessKey,
                    Status = NFeStatus.Pending,
                    XMotivo = "The transmission failed with an unknown outcome; SEFAZ must be consulted.",
                    IsTransient = true,

                    XmlContent = signedXml ?? string.Empty
                };

                ReadConsumerQr(unknown);
                return unknown;
            }

            var result = ReadAuthorization(context, service);

            if (string.IsNullOrWhiteSpace(result.XmlContent) && !string.IsNullOrWhiteSpace(signedXml))
            {
                result.XmlContent = signedXml;
                ReadConsumerQr(result);
            }

            return result;
        }

        /// <inheritdoc />
        public async Task<NFeProviderResult> TransmitSignedAsync(
            string accessKey,
            string signedXml,
            EnterpriseFiscalSettings fiscalSettings,
            NFeDocumentType type,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(fiscalSettings);

            if (string.IsNullOrWhiteSpace(signedXml))
            {
                return new NFeProviderResult
                {
                    AccessKey = accessKey,
                    Status = NFeStatus.Error,
                    XMotivo = "There is no stored document to transmit.",
                    NeverTransmitted = true
                };
            }

            NFeAutorizacao service;
            try
            {
                var certificate = await _certificates.GetAsync(fiscalSettings.EnterpriseId, cancellationToken);

                var csc = type == NFeDocumentType.NFCe && _cscProvider != null
                    ? await _cscProvider.GetAsync(fiscalSettings.EnterpriseId, cancellationToken)
                    : null;

                var configuracao = UnimakeConfiguracaoFactory.Create(
                    fiscalSettings, type, certificate, csc, TipoEmissaoOf(signedXml));

                var document = new XmlDocument { XmlResolver = null };
                document.LoadXml(signedXml);

                service = new NFeAutorizacao(XMLUtility.Deserializar<EnviNFe>(document), configuracao);
            }
            catch (Exception exception) when (IsPreTransmissionFailure(exception))
            {
                return PreTransmissionFailure(accessKey, exception, "contingency transmit");
            }

            try
            {
                await Task.Run(service.Executar, cancellationToken);
            }
            catch (Exception exception)
            {
                Log.Logger.Error(exception,
                    "Contingency transmission failed for chave {AccessKey}.", Redact(accessKey));

                return new NFeProviderResult
                {
                    AccessKey = accessKey,
                    Status = NFeStatus.Pending,
                    XMotivo = "The contingency transmission failed with an unknown outcome; SEFAZ must be consulted.",
                    IsTransient = true,
                    XmlContent = signedXml
                };
            }

            var envelope = service.Result;
            var protocol = envelope?.ProtNFe?.InfProt;
            var cStat = protocol?.CStat ?? envelope?.CStat ?? 0;
            var verdict = SefazCStatMap.ForAuthorization(cStat);

            var result = new NFeProviderResult
            {
                AccessKey = accessKey,
                Status = verdict.Status,
                Cstat = Cstat(cStat),
                XMotivo = protocol?.XMotivo ?? envelope?.XMotivo ?? string.Empty,
                Protocol = protocol?.NProt ?? string.Empty,
                IsTransient = verdict.IsTransient,
                NeverTransmitted = false,
                IssuedAt = protocol is null ? null : ToUtc(protocol.DhRecbto),
                XmlContent = Xml(service.NfeProcResult) ?? signedXml
            };

            ReadConsumerQr(result);
            return result;
        }

        /// <summary>
        /// Reads tpEmis back off the stored document, so the configuration agrees with what was
        /// signed instead of with what a caller assumed.
        /// </summary>
        private static byte TipoEmissaoOf(string signedXml)
        {
            try
            {
                var document = new XmlDocument { XmlResolver = null };
                document.LoadXml(signedXml);

                var nodes = document.GetElementsByTagName("tpEmis");
                if (nodes.Count > 0 && byte.TryParse(nodes[0]!.InnerText, out var parsed))
                    return parsed;
            }
            catch (XmlException)
            {
            }

            return 1;
        }

        public async Task<NFeProviderResult> ConsultAsync(
            string accessKey, EnterpriseFiscalSettings fiscalSettings, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(fiscalSettings);

            NFeConsultaProtocolo service;
            try
            {
                var certificate = await _certificates.GetAsync(fiscalSettings.EnterpriseId, cancellationToken);
                var configuracao = UnimakeConfiguracaoFactory.Create(fiscalSettings, NFeDocumentType.NFe, certificate);

                service = new NFeConsultaProtocolo(
                    accessKey,
                    fiscalSettings.Environment == 1 ? TipoAmbiente.Producao : TipoAmbiente.Homologacao,
                    configuracao);
            }
            catch (Exception exception) when (IsPreTransmissionFailure(exception))
            {
                return PreTransmissionFailure(accessKey, exception, "consult");
            }

            try
            {
                await Task.Run(service.Executar, cancellationToken);
            }
            catch (Exception exception)
            {
                Log.Logger.Warning(exception, "NFe consult failed for chave {AccessKey}.", Redact(accessKey));

                return new NFeProviderResult { AccessKey = accessKey, Status = NFeStatus.Pending, IsTransient = true };
            }

            return ReadConsult(accessKey, service.Result);
        }

        public async Task<NFeProviderResult> CancelAsync(
            string accessKey,
            string protocol,
            string reason,
            EnterpriseFiscalSettings fiscalSettings,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(fiscalSettings);

            var justification = (reason ?? string.Empty).Trim();
            if (justification.Length is < MinimumJustificationLength or > MaximumJustificationLength)
            {
                return new NFeProviderResult
                {
                    AccessKey = accessKey,
                    Status = NFeStatus.Pending,
                    XMotivo = $"The cancellation justification must be between {MinimumJustificationLength} and " +
                              $"{MaximumJustificationLength} characters; it was {justification.Length}.",
                    NeverTransmitted = true
                };
            }

            if (string.IsNullOrWhiteSpace(protocol))
            {
                return new NFeProviderResult
                {
                    AccessKey = accessKey,
                    Status = NFeStatus.Pending,
                    XMotivo = "The document has no authorization protocol, so no cancellation event can reference it.",
                    NeverTransmitted = true
                };
            }

            NFeRecepcaoEvento service;
            try
            {
                var certificate = await _certificates.GetAsync(fiscalSettings.EnterpriseId, cancellationToken);
                var configuracao = UnimakeConfiguracaoFactory.Create(fiscalSettings, NFeDocumentType.NFe, certificate);

                service = new NFeRecepcaoEvento(BuildCancellationEvent(accessKey, protocol, justification, fiscalSettings), configuracao);
            }
            catch (Exception exception) when (IsPreTransmissionFailure(exception))
            {
                return PreTransmissionFailure(accessKey, exception, "cancellation");
            }

            try
            {
                await Task.Run(service.Executar, cancellationToken);
            }
            catch (Exception exception)
            {
                // Deliberately not Cancelled. A cancellation whose answer was lost may or may not have
                // been homologated, and recording it as done would leave a live note marked cancelled.
                Log.Logger.Error(exception, "NFe cancellation failed for chave {AccessKey}.", Redact(accessKey));

                return new NFeProviderResult
                {
                    AccessKey = accessKey,
                    Status = NFeStatus.Pending,
                    XMotivo = "The cancellation event failed with an unknown outcome.",
                    IsTransient = true
                };
            }

            return ReadCancellation(accessKey, service);
        }

        public async Task<NFeProviderResult> GetServiceStatusAsync(
            EnterpriseFiscalSettings fiscalSettings, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(fiscalSettings);

            NFeStatusServico service;
            try
            {
                var certificate = await _certificates.GetAsync(fiscalSettings.EnterpriseId, cancellationToken);
                var configuracao = UnimakeConfiguracaoFactory.Create(fiscalSettings, NFeDocumentType.NFe, certificate);

                var consulta = new ConsStatServ
                {
                    Versao = fiscalSettings.SchemaVersion,
                    TpAmb = fiscalSettings.Environment == 1 ? TipoAmbiente.Producao : TipoAmbiente.Homologacao,
                    CUF = ToUf(fiscalSettings.Uf),
                    XServ = "STATUS"
                };

                service = new NFeStatusServico(consulta, configuracao);
            }
            catch (Exception exception) when (IsPreTransmissionFailure(exception))
            {
                return PreTransmissionFailure(string.Empty, exception, "status");
            }

            try
            {
                await Task.Run(service.Executar, cancellationToken);
            }
            catch (Exception exception)
            {
                Log.Logger.Warning(exception, "NFe StatusServico call failed.");
                return new NFeProviderResult { Status = NFeStatus.Pending, XMotivo = "SEFAZ could not be reached.", IsTransient = true };
            }

            var result = service.Result;
            var inOperation = SefazCStatMap.IsServiceInOperation(result?.CStat ?? 0);

            return new NFeProviderResult
            {
                Status = NFeStatus.Pending,
                Cstat = Cstat(result?.CStat),
                XMotivo = result?.XMotivo ?? string.Empty,
                IsTransient = !inOperation,
                IssuedAt = result is null ? null : ToUtc(result.DhRecbto)
            };
        }

        /// <summary>
        /// Reads the authorization answer, preferring the per-note protocol over the batch envelope.
        /// </summary>
        private static NFeProviderResult ReadAuthorization(NFeIssueContext context, NFeAutorizacao service)
        {
            var envelope = service.Result;
            var protocol = envelope?.ProtNFe?.InfProt;
            var cStat = protocol?.CStat ?? envelope?.CStat ?? 0;
            var verdict = SefazCStatMap.ForAuthorization(cStat);

            var result = new NFeProviderResult
            {
                AccessKey = context.AccessKey,
                Status = verdict.Status,
                Cstat = Cstat(cStat),
                XMotivo = protocol?.XMotivo ?? envelope?.XMotivo ?? string.Empty,
                Protocol = protocol?.NProt ?? string.Empty,
                ReceiptNumber = envelope?.InfRec?.NRec ?? string.Empty,
                TotalValue = context.Order.TotalValue,
                IsTransient = verdict.IsTransient,

                NeverTransmitted = false,
                IssuedAt = protocol is null ? null : ToUtc(protocol.DhRecbto),

                XmlContent = Xml(service.NfeProcResult) ?? Xml(service.ConteudoXMLAssinado) ?? string.Empty
            };

            ReadConsumerQr(result);

            if (protocol is not null && !string.IsNullOrWhiteSpace(protocol.ChNFe)
                && !string.Equals(protocol.ChNFe, context.AccessKey, StringComparison.Ordinal))
            {
                Log.Logger.Error(
                    "SEFAZ answered chave {Answered} for a request sent with chave {Sent}; the protocol is not being trusted.",
                    Redact(protocol.ChNFe),
                    Redact(context.AccessKey));

                return new NFeProviderResult
                {
                    AccessKey = context.AccessKey,
                    Status = NFeStatus.Pending,
                    XMotivo = "SEFAZ answered for a different access key; the outcome is unknown and must be consulted.",
                    IsTransient = true
                };
            }

            return result;
        }

        /// <summary>
        /// Lifts <c>infNFeSupl/qrCode</c> and <c>urlChave</c> off the signed XML onto the result.
        /// </summary>
        private static void ReadConsumerQr(NFeProviderResult result)
        {
            if (string.IsNullOrWhiteSpace(result.XmlContent))
                return;

            try
            {
                var document = new XmlDocument { XmlResolver = null };
                document.LoadXml(result.XmlContent);

                var supl = document.GetElementsByTagName("infNFeSupl");
                if (supl.Count == 0)
                    return;

                foreach (XmlNode child in supl[0]!.ChildNodes)
                {
                    if (child.LocalName == "qrCode")
                        result.QrCode = child.InnerText?.Trim() ?? string.Empty;
                    else if (child.LocalName == "urlChave")
                        result.UrlChave = child.InnerText?.Trim() ?? string.Empty;
                }
            }
            catch (XmlException exception)
            {
                Log.Logger.Error(exception,
                    "Could not read infNFeSupl off an authorised note for chave {AccessKey}; it will be stored with no consumer QR.",
                    Redact(result.AccessKey));
            }
        }

        private static NFeProviderResult ReadConsult(string accessKey, RetConsSitNFe? result)
        {
            if (result is null)
            {
                return new NFeProviderResult { AccessKey = accessKey, Status = NFeStatus.Pending, IsTransient = true };
            }

            var verdict = SefazCStatMap.ForConsult(result.CStat);
            var protocol = result.ProtNFe?.InfProt;

            return new NFeProviderResult
            {
                AccessKey = accessKey,
                Status = verdict.Status,
                Cstat = Cstat(result.CStat),
                XMotivo = result.XMotivo ?? string.Empty,
                Protocol = protocol?.NProt ?? string.Empty,
                IsTransient = verdict.IsTransient,

                NeverTransmitted = verdict.NeverTransmitted,
                IssuedAt = protocol is not null ? ToUtc(protocol.DhRecbto) : ToUtc(result.DhRecbto)
            };
        }

        private static NFeProviderResult ReadCancellation(string accessKey, NFeRecepcaoEvento service)
        {
            var envelope = service.Result;
            var answer = envelope?.RetEvento?.FirstOrDefault()?.InfEvento;
            var cStat = answer?.CStat ?? envelope?.CStat ?? 0;
            var verdict = SefazCStatMap.ForCancellation(cStat);

            return new NFeProviderResult
            {
                AccessKey = accessKey,
                Status = verdict.Status,
                Cstat = Cstat(cStat),
                XMotivo = answer?.XMotivo ?? envelope?.XMotivo ?? string.Empty,
                Protocol = answer?.NProt ?? string.Empty,
                IsTransient = verdict.IsTransient,
                IssuedAt = answer is null ? null : ToUtc(answer.DhRegEvento)
            };
        }

        private static EnvEvento BuildCancellationEvent(
            string accessKey, string protocol, string justification, EnterpriseFiscalSettings settings)
        {
            var cnpj = DocumentValidationHelper.NormalizeDocument(settings.Cnpj);
            var now = DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(-3));
            const int sequence = 1;

            var infEvento = new InfEvento
            {
                Id = string.Create(
                    CultureInfo.InvariantCulture,
                    $"ID{(int)TipoEventoNFe.Cancelamento}{accessKey}{sequence:D2}"),
                COrgao = ToUf(settings.Uf),
                TpAmb = settings.Environment == 1 ? TipoAmbiente.Producao : TipoAmbiente.Homologacao,
                CNPJ = cnpj,
                ChNFe = accessKey,
                DhEvento = now,
                TpEvento = TipoEventoNFe.Cancelamento,
                NSeqEvento = sequence,
                VerEvento = EventVersion,
                DetEvento = new DetEventoCanc
                {
                    Versao = EventVersion,
                    DescEvento = CancellationDescription,
                    NProt = protocol,
                    XJust = justification
                }
            };

            return new EnvEvento
            {
                Versao = EventVersion,
                IdLote = now.ToString("yyyyMMddHHmmssfff", CultureInfo.InvariantCulture),
                Evento = [new Evento { Versao = EventVersion, InfEvento = infEvento }]
            };
        }

        /// <summary>
        /// A failure that provably happened before any byte left: no certificate, an incomplete
        /// fiscal profile, a document the schema rejects.
        /// </summary>
        private static bool IsPreTransmissionFailure(Exception exception) =>
            exception is NFeCertificateException
                or ValidarXMLException
                or ValidatorDFeException
                or InvalidOperationException
                or ArgumentException;

        private static NFeProviderResult PreTransmissionFailure(string accessKey, Exception exception, string stage)
        {
            Log.Logger.Error(exception, "NFe {Stage} could not be prepared for chave {AccessKey}.", stage, Redact(accessKey));

            return new NFeProviderResult
            {
                AccessKey = accessKey,
                Status = NFeStatus.Error,
                XMotivo = exception.Message,

                NeverTransmitted = true
            };
        }

        private static UFBrasil ToUf(string? uf) =>
            Enum.TryParse<UFBrasil>((uf ?? string.Empty).Trim().ToUpperInvariant(), out var parsed)
                ? parsed
                : throw new InvalidOperationException($"The fiscal profile's UF is '{uf}', which is not a Brazilian UF.");

        private static string Cstat(int? cStat) =>
            cStat is null or 0 ? string.Empty : cStat.Value.ToString(CultureInfo.InvariantCulture);

        private static DateTime? ToUtc(DateTimeOffset value) =>
            value == default ? null : value.UtcDateTime;

        private static string? Xml(XmlDocument? document) => document?.OuterXml;

        private static string? Xml(NfeProc? proc) => proc is null ? null : Xml(proc.GerarXML());

        /// <summary>
        /// The chave embeds the emitter's CNPJ and the note number, so only its tail reaches a log.
        /// </summary>
        private static string Redact(string? accessKey) =>
            string.IsNullOrWhiteSpace(accessKey) || accessKey.Length < 8 ? "(none)" : "…" + accessKey[^8..];
    }
}
