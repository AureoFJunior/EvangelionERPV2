using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.EntityFrameworkCore;
using Serilog;
using EvangelionERPV2.EnterpriseModule.Infra.Context;
using EvangelionERPV2.Shared.Configs;
using EvangelionERPV2.EnterpriseModule.Domain.Interface;
using EvangelionERPV2.Shared.Entities;
using EvangelionERPV2.EnterpriseModule.Domain.Repositories;
using EvangelionERPV2.EnterpriseModule.Application.Interface;
using EvangelionERPV2.EnterpriseModule.Application.Services;
using EvangelionERPV2.Shared.Utils;
using Amazon.SecretsManager;

namespace EvangelionERPV2.EnterpriseModule.Application.DI
{
    public class EnterpriseIoC
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
                services.AddDbContext<EnterpriseModuleDbContext>(options => options.UseSqlServer(kmsProvider.GetKMSKey(configuration.GetConnectionString("DefaultConnection") ?? string.Empty)));

                #endregion

                #region Mapper
                var mapper = MapperConfig.RegisterMaps().CreateMapper();
                services.AddSingleton(mapper);
                services.AddAutoMapper(config => { }, AppDomain.CurrentDomain.GetAssemblies());

                #endregion

                #region Repositorys
                services.AddTransient(typeof(IRepository<>), typeof(Repository<>));
                services.AddTransient(typeof(IRepository<Enterprise>), typeof(EnterpriseRepository));
                services.AddTransient(typeof(IEnterpriseRepository<Enterprise>), typeof(EnterpriseRepository));


                #endregion

                #region Services
                services.AddTransient(typeof(IEnterpriseService<Enterprise>), typeof(EnterpriseService));
                #endregion

                services.AddScoped(typeof(IUnitOfWork<EnterpriseModuleDbContext>), typeof(UnitOfWork<EnterpriseModuleDbContext>));
            }
            catch (Exception ex)
            {
                Log.Logger.Error($"Error at DI IoC Enterprise: {ex.Message}", ex);
            }

        }
    }
}