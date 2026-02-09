using Amazon.SecretsManager;
using EvangelionERPV2.NFeModule.Application.Configs;
using EvangelionERPV2.NFeModule.Application.Interface;
using EvangelionERPV2.NFeModule.Application.Providers;
using EvangelionERPV2.NFeModule.Application.Services;
using EvangelionERPV2.NFeModule.Domain.Interface;
using EvangelionERPV2.NFeModule.Domain.Repositories;
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

                var serviceProvider = services.BuildServiceProvider();
                var kmsProvider = serviceProvider.GetRequiredService<AWSKMSKeyProvider>();
                #endregion

                services.AddLogging();

                #region DataBase
                services.AddDbContextPool<AppDbContext>(options => options.UseSqlServer(kmsProvider.GetKMSKey(configuration.GetConnectionString("DefaultConnection") ?? string.Empty)));
                #endregion

                #region Mapper
                var mapper = MapperConfig.RegisterMaps().CreateMapper();
                services.AddSingleton(mapper);
                services.AddAutoMapper(config => { }, AppDomain.CurrentDomain.GetAssemblies());
                #endregion

                services.Configure<NFeSettings>(configuration.GetSection("NFeSettings"));

                #region Repositorys
                services.AddTransient(typeof(EvangelionERPV2.Shared.Repositories.IRepository<>), typeof(EvangelionERPV2.Shared.Repositories.Repository<>));
                services.AddTransient(typeof(EvangelionERPV2.Shared.Repositories.IRepository<NFeDocument>), typeof(NFeRepository));
                services.AddTransient(typeof(INFeRepository<NFeDocument>), typeof(NFeRepository));
                #endregion

                #region Providers
                services.AddSingleton<INFeProvider, StubNFeProvider>();
                #endregion

                #region Services
                services.AddScoped(typeof(INFeService<NFeDocument>), typeof(NFeService));
                #endregion

                services.AddScoped(typeof(EvangelionERPV2.Shared.Repositories.IUnitOfWork<AppDbContext>), typeof(EvangelionERPV2.Shared.Repositories.UnitOfWork<AppDbContext>));
            }
            catch (Exception ex)
            {
                Log.Logger.Error($"Error at DI IoC NFe: {ex.Message}", ex);
            }
        }
    }
}
