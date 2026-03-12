using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.EntityFrameworkCore;
using Serilog;
using EvangelionERPV2.Shared.Configs;
using EvangelionERPV2.CustomerModule.Domain.Interface;
using EvangelionERPV2.Shared.Entities;
using EvangelionERPV2.CustomerModule.Domain.Repositories;
using EvangelionERPV2.CustomerModule.Application.Interface;
using EvangelionERPV2.CustomerModule.Application.Services;
using EvangelionERPV2.Shared.Utils;
using Amazon.SecretsManager;
using EvangelionERPV2.Shared.Context;

namespace EvangelionERPV2.CustomerModule.Application.DI
{
    public class CustomerIoC
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

                #region Repositorys
                services.AddTransient(typeof(EvangelionERPV2.Shared.Repositories.IRepository<>), typeof(EvangelionERPV2.Shared.Repositories.Repository<>));
                services.AddTransient(typeof(EvangelionERPV2.Shared.Repositories.IRepository<Enterprise>), typeof(EnterpriseModule.Domain.Repositories.EnterpriseRepository));
                services.AddTransient(typeof(EvangelionERPV2.Shared.Repositories.IRepository<Customer>), typeof(CustomerRepository));
                services.AddTransient(typeof(ICustomerRepository<Customer>), typeof(CustomerRepository));


                #endregion

                #region Services
                services.AddTransient(typeof(ICustomerService<Customer>), typeof(CustomerService));


                #endregion

                services.AddScoped(typeof(EvangelionERPV2.Shared.Repositories.IUnitOfWork<AppDbContext>), typeof(EvangelionERPV2.Shared.Repositories.UnitOfWork<AppDbContext>));
            }
            catch (Exception ex)
            {
                Log.Logger.Error(ex, $"Error at DI IoC Customer: {ex.Message}");
            }

        }
    }
}
