using EvangelionERPV2.NFeModule.Application.Interface;
using EvangelionERPV2.Shared.Entities;
using EvangelionERPV2.Shared.Exceptions;
using EvangelionERPV2.Shared.Repositories;
using EvangelionERPV2.Shared.Utils;

namespace EvangelionERPV2.NFeModule.Application.Services
{
    /// <summary>
    /// Admin-only CRUD over the per-tenant fiscal profile. Structurally mirrors
    /// PayrollService.GetSettingsAsync/UpsertSettingsAsync.
    /// </summary>
    public class NFeFiscalSettingsService : INFeFiscalSettingsService
    {
        internal const short NFeModel = 55;
        internal const short NFCeModel = 65;

        private const byte ProductionEnvironment = 1;
        private const byte HomologationEnvironment = 2;

        /// <summary>
        /// v1 scope. Restricting this is also an SSRF control: the UF picks the SEFAZ endpoint,
        /// and an unrecognised UF must never reach the fiscal client.
        /// </summary>
        private static readonly IReadOnlyDictionary<string, short> SupportedUfs =
            new Dictionary<string, short>(StringComparer.OrdinalIgnoreCase)
            {
                ["SP"] = 35,
                ["PR"] = 41,
                ["SC"] = 42,
                ["RS"] = 43
            };

        private readonly IRepository<EnterpriseFiscalSettings> _settingsRepository;
        private readonly IRepository<NFeNumberSequence> _sequenceRepository;

        public NFeFiscalSettingsService(
            IRepository<EnterpriseFiscalSettings> settingsRepository,
            IRepository<NFeNumberSequence> sequenceRepository)
        {
            _settingsRepository = settingsRepository;
            _sequenceRepository = sequenceRepository;
        }

        public async Task<EnterpriseFiscalSettings> GetSettingsAsync(Guid enterpriseId)
        {
            if (enterpriseId == Guid.Empty)
                throw new InsertDatabaseException("Fiscal settings enterprise is required.");

            var settings = await FindActiveSettingsAsync(enterpriseId);

            // A tenant that has never configured anything gets a transient default rather than a
            // null, so the settings screen renders on first visit.
            return settings ?? new EnterpriseFiscalSettings { EnterpriseId = enterpriseId };
        }

        public async Task<EnterpriseFiscalSettings> UpsertSettingsAsync(Guid enterpriseId, EnterpriseFiscalSettings settings)
        {
            if (settings == null)
                throw new InsertDatabaseException($"{nameof(EnterpriseFiscalSettings)} is null.");

            if (enterpriseId == Guid.Empty)
                throw new InsertDatabaseException("Fiscal settings enterprise is required.");

            Normalize(settings);
            Validate(settings);

            var now = DateTime.UtcNow;
            var existing = await FindActiveSettingsAsync(enterpriseId);

            // The profile and its counter rows are one unit of work. Committing them separately left
            // a window where a saved profile had no counters, and issuance would then report the
            // tenant as unconfigured with no way to tell that from a genuinely missing profile.
            await _settingsRepository.ExecuteInTransactionAsync(async () =>
            {
                if (existing == null)
                {
                    existing = new EnterpriseFiscalSettings
                    {
                        Id = Guid.NewGuid(),
                        EnterpriseId = enterpriseId,
                        CreatedAt = now,
                        IsActive = true
                    };
                    Apply(existing, settings, now);
                    await _settingsRepository.CreateAsync(existing);
                }
                else
                {
                    Apply(existing, settings, now);
                    _settingsRepository.Update(existing);
                }

                await _settingsRepository.CommitAsync();

                await EnsureNumberSequencesAsync(enterpriseId, existing, now);
            });

            return existing!;
        }

        private async Task<EnterpriseFiscalSettings?> FindActiveSettingsAsync(Guid enterpriseId)
        {
            return (await _settingsRepository.GetAllAsync(
                    x => x.IsActive == true && x.EnterpriseId == enterpriseId))
                .FirstOrDefault();
        }

        /// <summary>
        /// Creates the counter rows lazily, here rather than on the issuance path, so that
        /// allocation stays a single atomic UPDATE with nothing to create under contention.
        /// Existing rows are never reset — a counter that restarted would re-issue numbers
        /// already sent to SEFAZ.
        /// </summary>
        private async Task EnsureNumberSequencesAsync(Guid enterpriseId, EnterpriseFiscalSettings settings, DateTime now)
        {
            var wanted = new[]
            {
                (Model: NFeModel, Series: settings.NFeSeries),
                (Model: NFCeModel, Series: settings.NFCeSeries)
            };

            var created = false;

            foreach (var (model, series) in wanted)
            {
                foreach (var environment in new[] { ProductionEnvironment, HomologationEnvironment })
                {
                    if (await SequenceExistsAsync(enterpriseId, model, series, environment))
                        continue;

                    await _sequenceRepository.CreateAsync(new NFeNumberSequence
                    {
                        Id = Guid.NewGuid(),
                        EnterpriseId = enterpriseId,
                        Model = model,
                        Series = series,
                        Environment = environment,
                        NextNumber = 1,
                        CreatedAt = now,
                        IsActive = true
                    });
                    created = true;
                }
            }

            if (created)
                await _sequenceRepository.CommitAsync();
        }

        private async Task<bool> SequenceExistsAsync(Guid enterpriseId, short model, string series, byte environment)
        {
            return (await _sequenceRepository.GetAllAsync(
                    x => x.EnterpriseId == enterpriseId
                         && x.Model == model
                         && x.Series == series
                         && x.Environment == environment))
                .Any();
        }

        private static void Normalize(EnterpriseFiscalSettings settings)
        {
            settings.Cnpj = DocumentValidationHelper.NormalizeDocument(settings.Cnpj);
            settings.InscricaoEstadual = KeepDigits(settings.InscricaoEstadual);
            settings.InscricaoMunicipal = NullIfBlank(KeepDigits(settings.InscricaoMunicipal));
            settings.Cep = KeepDigits(settings.Cep);
            settings.CodigoMunicipioIbge = KeepDigits(settings.CodigoMunicipioIbge);
            settings.Telefone = NullIfBlank(KeepDigits(settings.Telefone));

            settings.Uf = (settings.Uf ?? string.Empty).Trim().ToUpperInvariant();
            settings.RazaoSocial = (settings.RazaoSocial ?? string.Empty).Trim();
            settings.NomeFantasia = NullIfBlank(settings.NomeFantasia?.Trim());
            settings.NFeSeries = BlankTo(settings.NFeSeries?.Trim(), "1");
            settings.NFCeSeries = BlankTo(settings.NFCeSeries?.Trim(), "1");
            settings.SchemaVersion = BlankTo(settings.SchemaVersion?.Trim(), "4.00");
            settings.CodigoPais = BlankTo(settings.CodigoPais?.Trim(), "1058");
            settings.Pais = BlankTo(settings.Pais?.Trim(), "BRASIL");
            settings.DefaultUnidadeTributavel = BlankTo(settings.DefaultUnidadeTributavel?.Trim(), "UN");

            // natOp is mandatory on the wire and free text, so a tenant that clears it gets the
            // default back rather than a note SEFAZ refuses for an empty element.
            settings.DefaultNatOp = BlankTo(settings.DefaultNatOp?.Trim(), "VENDA DE MERCADORIA");

            // The client never supplies cUF — it is derived from the UF so the two cannot drift.
            if (SupportedUfs.TryGetValue(settings.Uf, out var codigoUf))
                settings.CodigoUfIbge = codigoUf;
        }

        private static void Validate(EnterpriseFiscalSettings settings)
        {
            if (!DocumentValidationHelper.BeValidCnpj(settings.Cnpj))
                throw new InsertDatabaseException("Fiscal settings CNPJ is invalid.");

            if (string.IsNullOrWhiteSpace(settings.InscricaoEstadual))
                throw new InsertDatabaseException("Fiscal settings inscricao estadual is required.");

            if (string.IsNullOrWhiteSpace(settings.RazaoSocial))
                throw new InsertDatabaseException("Fiscal settings razao social is required.");

            if (!SupportedUfs.ContainsKey(settings.Uf))
                throw new InsertDatabaseException("Fiscal settings UF must be one of SP, RS, SC or PR.");

            if (settings.Crt is < 1 or > 4)
                throw new InsertDatabaseException("Fiscal settings CRT must be between 1 and 4.");

            // Simples Nacional (CRT 1/2) carries CSOSN, Regime Normal (CRT 3) carries CST. Saving
            // a profile without the one its regime needs fails at emission, after burning a number.
            if (settings.Crt == 3 && string.IsNullOrWhiteSpace(settings.DefaultCst))
                throw new InsertDatabaseException("Fiscal settings default CST is required for Regime Normal.");

            if (settings.Crt is 1 or 2 && string.IsNullOrWhiteSpace(settings.DefaultCsosn))
                throw new InsertDatabaseException("Fiscal settings default CSOSN is required for Simples Nacional.");

            if (settings.Environment is not (ProductionEnvironment or HomologationEnvironment))
                throw new InsertDatabaseException("Fiscal settings environment must be 1 (producao) or 2 (homologacao).");

            ValidateAddress(settings);
            ValidateFiscalDefaults(settings);
            ValidateDeadlines(settings);
        }

        private static void ValidateAddress(EnterpriseFiscalSettings settings)
        {
            if (string.IsNullOrWhiteSpace(settings.Logradouro))
                throw new InsertDatabaseException("Fiscal settings logradouro is required.");

            if (string.IsNullOrWhiteSpace(settings.Numero))
                throw new InsertDatabaseException("Fiscal settings numero is required.");

            if (string.IsNullOrWhiteSpace(settings.Bairro))
                throw new InsertDatabaseException("Fiscal settings bairro is required.");

            if (string.IsNullOrWhiteSpace(settings.Municipio))
                throw new InsertDatabaseException("Fiscal settings municipio is required.");

            if (settings.CodigoMunicipioIbge.Length != 7)
                throw new InsertDatabaseException("Fiscal settings IBGE municipality code must have 7 digits.");

            // The IBGE code embeds the state in its first two digits; a mismatch means the
            // municipality belongs to another UF and SEFAZ rejects the note.
            if (!settings.CodigoMunicipioIbge.StartsWith(
                    settings.CodigoUfIbge.ToString(), StringComparison.Ordinal))
                throw new InsertDatabaseException("Fiscal settings IBGE municipality code does not belong to the selected UF.");

            if (settings.Cep.Length != 8)
                throw new InsertDatabaseException("Fiscal settings CEP must have 8 digits.");
        }

        private static void ValidateFiscalDefaults(EnterpriseFiscalSettings settings)
        {
            RequireCfop(settings.DefaultCfopIntra, nameof(settings.DefaultCfopIntra));
            RequireCfop(settings.DefaultCfopInter, nameof(settings.DefaultCfopInter));

            if (settings.DefaultOrigem > 8)
                throw new InsertDatabaseException("Fiscal settings default origem must be between 0 and 8.");

            RequireRate(settings.DefaultAliquotaIcms, "ICMS");
            RequireRate(settings.DefaultAliquotaPis, "PIS");
            RequireRate(settings.DefaultAliquotaCofins, "COFINS");

            if (IsBlankOrWrongLength(settings.NFeSeries, 3) || IsBlankOrWrongLength(settings.NFCeSeries, 3))
                throw new InsertDatabaseException("Fiscal settings series must have between 1 and 3 characters.");
        }

        private static void ValidateDeadlines(EnterpriseFiscalSettings settings)
        {
            if (settings.NFeCancellationWindowMinutes is < 1 or > 1440)
                throw new InsertDatabaseException("NFe cancellation window must be between 1 and 1440 minutes.");

            if (settings.NFCeCancellationWindowMinutes is < 1 or > 1440)
                throw new InsertDatabaseException("NFCe cancellation window must be between 1 and 1440 minutes.");

            if (settings.NFCeCustomerIdentificationThreshold < 0m)
                throw new InsertDatabaseException("NFCe customer identification threshold cannot be negative.");
        }

        private static void RequireCfop(string? cfop, string field)
        {
            if (string.IsNullOrWhiteSpace(cfop) || cfop.Length != 4 || !cfop.All(char.IsDigit))
                throw new InsertDatabaseException($"Fiscal settings {field} must have 4 digits.");
        }

        private static void RequireRate(decimal rate, string tax)
        {
            if (rate is < 0m or > 1m)
                throw new InsertDatabaseException($"Fiscal settings default {tax} rate must be between 0 and 1.");
        }

        private static void Apply(EnterpriseFiscalSettings target, EnterpriseFiscalSettings source, DateTime now)
        {
            target.Cnpj = source.Cnpj;
            target.InscricaoEstadual = source.InscricaoEstadual;
            target.InscricaoMunicipal = source.InscricaoMunicipal;
            target.RazaoSocial = source.RazaoSocial;
            target.NomeFantasia = source.NomeFantasia;
            target.Crt = source.Crt;

            target.Logradouro = source.Logradouro;
            target.Numero = source.Numero;
            target.Complemento = source.Complemento;
            target.Bairro = source.Bairro;
            target.CodigoMunicipioIbge = source.CodigoMunicipioIbge;
            target.Municipio = source.Municipio;
            target.Uf = source.Uf;
            target.CodigoUfIbge = source.CodigoUfIbge;
            target.Cep = source.Cep;
            target.CodigoPais = source.CodigoPais;
            target.Pais = source.Pais;
            target.Telefone = source.Telefone;

            target.Environment = source.Environment;
            target.Enabled = source.Enabled;
            target.NFCeEnabled = source.NFCeEnabled;
            target.NFeSeries = source.NFeSeries;
            target.NFCeSeries = source.NFCeSeries;
            target.CscId = source.CscId;
            target.SchemaVersion = source.SchemaVersion;

            target.NFeCancellationWindowMinutes = source.NFeCancellationWindowMinutes;
            target.NFCeCancellationWindowMinutes = source.NFCeCancellationWindowMinutes;
            target.NFCeCustomerIdentificationThreshold = source.NFCeCustomerIdentificationThreshold;

            target.DefaultNatOp = source.DefaultNatOp;
            target.DefaultCfopIntra = source.DefaultCfopIntra;
            target.DefaultCfopInter = source.DefaultCfopInter;
            target.DefaultCst = source.DefaultCst;
            target.DefaultCsosn = source.DefaultCsosn;
            target.DefaultOrigem = source.DefaultOrigem;
            target.DefaultAliquotaIcms = source.DefaultAliquotaIcms;
            target.AliquotaCreditoSn = source.AliquotaCreditoSn;
            target.DefaultCstPis = source.DefaultCstPis;
            target.DefaultAliquotaPis = source.DefaultAliquotaPis;
            target.DefaultCstCofins = source.DefaultCstCofins;
            target.DefaultAliquotaCofins = source.DefaultAliquotaCofins;
            target.DefaultUnidadeTributavel = source.DefaultUnidadeTributavel;

            // Certificate metadata is owned by the certificate upload flow, never by this payload.
            target.UpdatedAt = now;
        }

        private static string KeepDigits(string? value) => DocumentValidationHelper.NormalizeDocument(value);

        private static string? NullIfBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;

        private static string BlankTo(string? value, string fallback) =>
            string.IsNullOrWhiteSpace(value) ? fallback : value;

        private static bool IsBlankOrWrongLength(string? value, int maxLength) =>
            string.IsNullOrWhiteSpace(value) || value.Length > maxLength;
    }
}
