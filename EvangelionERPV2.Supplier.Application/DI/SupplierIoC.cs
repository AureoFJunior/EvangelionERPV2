using Amazon.SecretsManager;
using EvangelionERPV2.Shared.Configs;
using EvangelionERPV2.Shared.Context;
using EvangelionERPV2.Shared.Entities;
using EvangelionERPV2.Shared.Utils;
using EvangelionERPV2.SupplierModule.Application.Interface;
using EvangelionERPV2.SupplierModule.Application.Services;
using EvangelionERPV2.SupplierModule.Domain.Interface;
using EvangelionERPV2.SupplierModule.Domain.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Serilog;

namespace EvangelionERPV2.SupplierModule.Application.DI
{
    public class SupplierIoC
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

                #region Repositorys
                services.AddTransient(typeof(EvangelionERPV2.Shared.Repositories.IRepository<>), typeof(EvangelionERPV2.Shared.Repositories.Repository<>));
                services.AddTransient(typeof(EvangelionERPV2.Shared.Repositories.IRepository<Supplier>), typeof(SupplierRepository));
                services.AddTransient(typeof(ISupplierRepository<Supplier>), typeof(SupplierRepository));
                #endregion

                #region Services
                services.AddTransient(typeof(ISupplierService<Supplier>), typeof(SupplierService));
                #endregion

                services.AddScoped(typeof(EvangelionERPV2.Shared.Repositories.IUnitOfWork<AppDbContext>), typeof(EvangelionERPV2.Shared.Repositories.UnitOfWork<AppDbContext>));
            }
            catch (Exception ex)
            {
                Log.Logger.Error("Error at DI IoC Supplier. ErrorType={ErrorType}", ex.GetType().Name);
            }
        }
    }
}
