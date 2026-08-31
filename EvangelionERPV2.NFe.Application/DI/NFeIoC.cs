using Amazon.SecretsManager;
using EvangelionERPV2.NFeModule.Application.Configs;
using EvangelionERPV2.NFeModule.Application.Interface;
using EvangelionERPV2.NFeModule.Application.Providers;
using EvangelionERPV2.NFeModule.Infra.Providers;
using EvangelionERPV2.NFeModule.Application.Services;
using EvangelionERPV2.NFeModule.Domain.Interface;
using EvangelionERPV2.NFeModule.Domain.Repositories;
using EvangelionERPV2.NFeModule.Infra.Certificates;
using EvangelionERPV2.NFeModule.Infra.Sefaz;
using Microsoft.Extensions.Caching.Memory;
using EvangelionERPV2.Shared.Configs;
using EvangelionERPV2.Shared.Context;
using EvangelionERPV2.Shared.Entities;
using EvangelionERPV2.Shared.Utils;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Serilog;

namespace EvangelionERPV2.NFeModule.Application.DI
{
    public class NFeIoC
    {
        public static void Configure(IServiceCollection services, IConfiguration configuration)
        {
            try
            {
                #region AWS
                services.AddSingleton<IAmazonSecretsManager, AmazonSecretsManagerClient>(sp =>
                {
                    var region = Amazon.RegionEndpoint.USEast1;
                    return new AmazonSecretsManagerClient(region);
                });
                services.AddSingleton<AWSKMSKeyProvider>();
                #endregion

                services.AddLogging();

                #region DataBase
                services.AddDbContextPool<AppDbContext>((serviceProvider, options) =>
                {
                    var kmsProvider = serviceProvider.GetRequiredService<AWSKMSKeyProvider>();
                    options.UseSqlServer(kmsProvider.GetKMSKey(configuration.GetConnectionString("DefaultConnection") ?? string.Empty));
                });
                #endregion

                #region Mapper
                var mapper = MapperConfig.RegisterMaps().CreateMapper();
                services.AddSingleton(mapper);
                services.AddAutoMapper(config => { }, AppDomain.CurrentDomain.GetAssemblies());
                #endregion

                services.Configure<NFeSettings>(configuration.GetSection("NFeSettings"));

                // infRespTec: who wrote the software, installation-wide. Empty until the values in
                // docs/fiscal/TODO-manual.md M13 are supplied, and an empty group is omitted.
                services.Configure<EvangelionERPV2.NFeModule.Infra.Sefaz.NFeRespTecSettings>(
                    configuration.GetSection("NFeRespTecSettings"));

                #region Repositorys
                services.AddTransient(typeof(EvangelionERPV2.Shared.Repositories.IRepository<>), typeof(EvangelionERPV2.Shared.Repositories.Repository<>));
                services.AddTransient(typeof(EvangelionERPV2.Shared.Repositories.IRepository<NFeDocument>), typeof(NFeRepository));
                services.AddTransient(typeof(INFeRepository<NFeDocument>), typeof(NFeRepository));
                #endregion

                #region Providers
                // Scoped, not singleton: it resolves a per-tenant certificate on every call and
                // must not outlive the request that chose the tenant. StubNFeProvider stays in the
                // tree until phase 9 deletes it (docs/fiscal/SPEC-remaining-code.md §2.6); nothing
                // registers it any more.
                services.AddScoped<INFeProvider, SefazNFeProvider>();

                services.AddMemoryCache();

                // Singleton: it holds the materialised certificate cache, which must be shared
                // across requests. It has no per-request state of its own.
                services.AddSingleton<IEnterpriseCertificateProvider>(serviceProvider =>
                {
                    var certificateSecretPrefix =
                        configuration.GetValue<string>("NFeSettings:CertificateSecretPrefix")
                        ?? new NFeSettings().CertificateSecretPrefix;

                    return new EnterpriseCertificateProvider(
                        serviceProvider.GetRequiredService<IAmazonSecretsManager>(),
                        serviceProvider.GetRequiredService<AWSKMSKeyProvider>(),
                        serviceProvider.GetRequiredService<IMemoryCache>(),
                        certificateSecretPrefix);
                });

                // Singleton for the same reason as the certificate provider: it owns a cache that
                // must be shared, and holds no per-request state.
                services.AddSingleton<IEnterpriseCscProvider>(serviceProvider =>
                {
                    var cscSecretPrefix =
                        configuration.GetValue<string>("NFeSettings:CscSecretPrefix")
                        ?? new NFeSettings().CscSecretPrefix;

                    return new EnterpriseCscProvider(
                        serviceProvider.GetRequiredService<IAmazonSecretsManager>(),
                        serviceProvider.GetRequiredService<AWSKMSKeyProvider>(),
                        serviceProvider.GetRequiredService<IMemoryCache>(),
                        cscSecretPrefix);
                });
                #endregion

                #region Services
                services.AddScoped(typeof(INFeService<NFeDocument>), typeof(NFeService));
                services.AddScoped<INFeFiscalSettingsService, NFeFiscalSettingsService>();
                services.AddScoped<INFeCertificateService, NFeCertificateService>();

                // Inbound notas: the XML becomes a payable. Registered here because the parsing
                // lives in this module; the bill it creates goes through Bills' own service.
                services.AddScoped<ISupplierNFeImportService, SupplierNFeImportService>();

                // Concrete, no interface: the worker below is its only caller. The sweeper is the
                // only owner of a document left Pending with the write-ahead marker set, so it is
                // registered here rather than in the host — the module that can create that state
                // is the module that has to clear it.
                services.AddScoped<NFeReconciliationService>();
                services.AddHostedService<NFeReconciliationWorker>();
                #endregion

                // A swallowed DI failure in this module means silent non-issuance, so the fiscal
                // registrations are logged rather than left to the catch below.
                Log.Logger.Information("NFe fiscal settings and certificate services registered.");

                services.AddScoped(typeof(EvangelionERPV2.Shared.Repositories.IUnitOfWork<AppDbContext>), typeof(EvangelionERPV2.Shared.Repositories.UnitOfWork<AppDbContext>));
            }
            catch (Exception ex)
            {
                Log.Logger.Error("Error at DI IoC NFe. ErrorType={ErrorType}", ex.GetType().Name);
            }
        }
    }
}
