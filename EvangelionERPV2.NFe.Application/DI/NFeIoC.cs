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
        public static void Configure(IServiceCollection services, IConfiguration configuration, bool useStubProvider = false)
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

                services.Configure<EvangelionERPV2.NFeModule.Infra.Sefaz.NFeRespTecSettings>(
                    configuration.GetSection("NFeRespTecSettings"));

                #region Repositorys
                services.AddTransient(typeof(EvangelionERPV2.Shared.Repositories.IRepository<>), typeof(EvangelionERPV2.Shared.Repositories.Repository<>));
                services.AddTransient(typeof(EvangelionERPV2.Shared.Repositories.IRepository<NFeDocument>), typeof(NFeRepository));
                services.AddTransient(typeof(INFeRepository<NFeDocument>), typeof(NFeRepository));
                #endregion

                #region Providers
                if (useStubProvider)
                    services.AddScoped<INFeProvider, StubNFeProvider>();
                else
                    services.AddScoped<INFeProvider, SefazNFeProvider>();

                services.AddMemoryCache();

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

                services.AddScoped<ISupplierNFeImportService, SupplierNFeImportService>();

                services.AddScoped<NFeReconciliationService>();
                services.AddHostedService<NFeReconciliationWorker>();

                services.AddScoped<NFeContingencyService>();
                services.AddHostedService<NFeContingencyWorker>();
                #endregion

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
