using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.EntityFrameworkCore;
using Serilog;
using EvangelionERPV2.Shared.Configs;
using EvangelionERPV2.CustomerModule.Infra.Context;
using EvangelionERPV2.CustomerModule.Domain.Interface;
using EvangelionERPV2.Shared.Entities;
using EvangelionERPV2.CustomerModule.Domain.Repositories;
using EvangelionERPV2.CustomerModule.Application.Interface;
using EvangelionERPV2.CustomerModule.Application.Services;
using EvangelionERPV2.Shared.Utils;
using Amazon.SecretsManager;

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
                services.AddDbContext<CustomerModuleDbContext>(options => options.UseSqlServer(kmsProvider.GetKMSKey(configuration.GetConnectionString("DefaultConnection") ?? string.Empty)));

                #endregion

                #region Mapper
                var mapper = MapperConfig.RegisterMaps().CreateMapper();
                services.AddSingleton(mapper);
                services.AddAutoMapper(config => { }, AppDomain.CurrentDomain.GetAssemblies());

                #endregion

                #region Repositorys
                services.AddTransient(typeof(IRepository<>), typeof(Domain.Repositories.Repository<>));
                services.AddTransient(typeof(EnterpriseModule.Domain.Interface.IRepository<Enterprise>), typeof(EnterpriseModule.Domain.Repositories.EnterpriseRepository));
                services.AddTransient(typeof(IRepository<Customer>), typeof(CustomerRepository));
                services.AddTransient(typeof(ICustomerRepository<Customer>), typeof(CustomerRepository));


                #endregion

                #region Services
                services.AddTransient(typeof(ICustomerService<Customer>), typeof(CustomerService));


                #endregion

                services.AddScoped(typeof(IUnitOfWork<CustomerModuleDbContext>), typeof(Domain.Repositories.UnitOfWork<CustomerModuleDbContext>));
            }
            catch (Exception ex)
            {
                Log.Logger.Error($"Error at DI IoC Customer: {ex.Message}", ex);
            }

        }
    }
}