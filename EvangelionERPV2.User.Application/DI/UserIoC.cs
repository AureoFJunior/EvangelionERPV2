using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.EntityFrameworkCore;
using Serilog;
using EvangelionERPV2.Shared.Configs;
using EvangelionERPV2.Shared.Utils;
using EvangelionERPV2.UserModule.Infra.Context;
using EvangelionERPV2.UserModule.Domain.Interface;
using EvangelionERPV2.Shared.Entities;
using EvangelionERPV2.UserModule.Domain.Repositories;
using EvangelionERPV2.UserModule.Application.Token;
using EvangelionERPV2.UserModule.Application.Interface;
using EvangelionERPV2.UserModule.Application.Services;
using Amazon.SecretsManager;

namespace EvangelionERPV2.UserModule.Application.DI
{
    public class UserIoC
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
                services.AddDbContext<UserModuleDbContext>(options =>
                {
                    var connectionString = kmsProvider.GetKMSKey(configuration.GetConnectionString("DefaultConnection") ?? string.Empty);

                    options.UseSqlServer(connectionString, sqlServerOptionsAction: sqlOptions =>
                    {
                        sqlOptions.EnableRetryOnFailure(
                            maxRetryCount: 3,
                            maxRetryDelay: TimeSpan.FromSeconds(10),
                            errorNumbersToAdd: null);
                    });
                });

                #endregion

                #region Mapper
                var mapper = MapperConfig.RegisterMaps().CreateMapper();
                services.AddSingleton(mapper);
                services.AddAutoMapper(config => { }, AppDomain.CurrentDomain.GetAssemblies());

                #endregion

                #region Repositorys
                services.AddTransient(typeof(IRepository<>), typeof(Repository<>));
                services.AddTransient(typeof(IRepository<User>), typeof(UserRepository));
                #endregion

                #region Services
                services.AddTransient(typeof(TokenService));
                services.AddTransient(typeof(IUserService<User>), typeof(UserService));
                #endregion

                services.AddScoped(typeof(IUnitOfWork<UserModuleDbContext>), typeof(UnitOfWork<UserModuleDbContext>));
            }
            catch (Exception ex)
            {
                Log.Logger.Error($"Error at DI User IoC: {ex.Message}", ex);
            }

        }
    }
}