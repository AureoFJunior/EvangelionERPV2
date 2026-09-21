using Amazon.SecretsManager;
using EvangelionERPV2.PdvModule.Application.Interface;
using EvangelionERPV2.PdvModule.Application.Services;
using EvangelionERPV2.Shared.Configs;
using EvangelionERPV2.Shared.Context;
using EvangelionERPV2.Shared.Repositories;
using EvangelionERPV2.Shared.Utils;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Serilog;

namespace EvangelionERPV2.PdvModule.Application.DI
{
    public class PdvIoC
    {
        public static void Configure(IServiceCollection services, IConfiguration configuration)
        {
            try
            {
                #region AWS
                services.AddSingleton<IAmazonSecretsManager>(sp => new AmazonSecretsManagerClient(Amazon.RegionEndpoint.USEast1));
                services.AddSingleton<AWSKMSKeyProvider>();
                #endregion

                services.AddLogging();

                #region DataBase
                var kmsProvider = new AWSKMSKeyProvider(new AmazonSecretsManagerClient(Amazon.RegionEndpoint.USEast1), configuration);
                services.AddDbContextPool<AppDbContext>(options =>
                    options.UseSqlServer(kmsProvider.GetKMSKey(configuration.GetConnectionString("DefaultConnection") ?? string.Empty)));
                #endregion

                #region Mapper
                var mapper = MapperConfig.RegisterMaps().CreateMapper();
                services.AddSingleton(mapper);
                services.AddAutoMapper(config => { }, AppDomain.CurrentDomain.GetAssemblies());
                #endregion

                #region Repositorys
                services.AddTransient(typeof(IRepository<>), typeof(Repository<>));
                #endregion

                #region Services
                services.AddScoped<IPdvService, PdvService>();

                services.AddScoped<StripePixProvider>();
                services.AddScoped<StripeWebhookHandler>();
                #endregion

                services.AddScoped(typeof(IUnitOfWork<AppDbContext>), typeof(UnitOfWork<AppDbContext>));
            }
            catch (Exception ex)
            {
                Log.Logger.Error("Error at DI IoC Pdv. ErrorType={ErrorType}", ex.GetType().Name);
            }
        }
    }
}
