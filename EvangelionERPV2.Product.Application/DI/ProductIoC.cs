using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.EntityFrameworkCore;
using Serilog;
using EvangelionERPV2.ProductModule.Domain.Interface;
using EvangelionERPV2.ProductModule.Domain.Repositories;
using EvangelionERPV2.Shared.Entities;
using EvangelionERPV2.Shared.Configs;
using EvangelionERPV2.ProductModule.Application.Interface;
using EvangelionERPV2.ProductModule.Infra.Context;
using EvangelionERPV2.ProductModule.Application.Services;
using Amazon.SecretsManager;
using EvangelionERPV2.Shared.Utils;

namespace EvangelionERPV2.ProductModule.Application.DI
{
    public class ProductIoC
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
                services.AddDbContext<ProductModuleDbContext>(options => options.UseSqlServer(kmsProvider.GetKMSKey(configuration.GetConnectionString("DefaultConnection") ?? string.Empty)));

                #endregion

                #region Mapper
                var mapper = MapperConfig.RegisterMaps().CreateMapper();
                services.AddSingleton(mapper);
                services.AddAutoMapper(AppDomain.CurrentDomain.GetAssemblies());

                #endregion

                #region Repositorys
                services.AddTransient(typeof(IRepository<>), typeof(Repository<>));
                services.AddTransient(typeof(IRepository<Product>), typeof(ProductRepository));
                services.AddTransient(typeof(IRepository<OrderedProduct>), typeof(OrderedProductRepository));
                services.AddTransient(typeof(IProductRepository<Product>), typeof(ProductRepository));


                #endregion

                #region Services
                services.AddTransient(typeof(IProductService<Product>), typeof(ProductService));
                services.AddTransient(typeof(IOrderedProductService<OrderedProduct>), typeof(OrderedProductService));
                #endregion

                services.AddScoped(typeof(IUnitOfWork<ProductModuleDbContext>), typeof(UnitOfWork<ProductModuleDbContext>));

                #region Redis
                services.AddStackExchangeRedisCache(o =>
                {
                    o.InstanceName = kmsProvider.GetKMSKey(configuration.GetSection("RedisSettings")["InstanceName"] ?? string.Empty);
                    o.Configuration = kmsProvider.GetKMSKey(configuration.GetSection("RedisSettings")["ConnectionString"] ?? string.Empty);
                });
                #endregion
            }
            catch (Exception ex)
            {
                Log.Logger.Error($"Error at DI IoC: {ex.Message}", ex);
            }

        }
    }
}