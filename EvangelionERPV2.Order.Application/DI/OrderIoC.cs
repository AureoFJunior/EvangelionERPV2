using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.EntityFrameworkCore;
using Serilog;
using EvangelionERPV2.OrderModule.Infra.Context;
using EvangelionERPV2.OrderModule.Domain.Repositories;
using EvangelionERPV2.OrderModule.Domain.Interface;
using EvangelionERPV2.Shared.Configs;
using Amazon.SecretsManager;
using EvangelionERPV2.Shared.Utils;
using EvangelionERPV2.Shared.Entities.RabbitMQ;
using EvangelionERPV2.Shared.Entities;
using EvangelionERPV2.OrderModule.Application.Interface;
using EvangelionERPV2.OrderModule.Application.Services;
using EvangelionERPV2.ProductModule.Domain.Repositories;
using EvangelionERPV2.ProductModule.Domain.Interface;

namespace EvangelionERPV2.OrderModule.Application.DI
{
    public class OrderIoC
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
                services.AddDbContext<OrderModuleDbContext>(options => options.UseSqlServer(kmsProvider.GetKMSKey(configuration.GetConnectionString("DefaultConnection") ?? string.Empty)));

                #endregion

                #region Mapper
                var mapper = MapperConfig.RegisterMaps().CreateMapper();
                services.AddSingleton(mapper);
                services.AddAutoMapper(AppDomain.CurrentDomain.GetAssemblies());

                #endregion

                #region Repositorys
                services.AddTransient(typeof(Domain.Interface.IRepository<>), typeof(Domain.Repositories.Repository<>));
                services.AddTransient(typeof(ProductModule.Domain.Interface.IRepository<Product>), typeof(ProductRepository));
                services.AddTransient(typeof(Domain.Interface.IRepository<Order>), typeof(OrderRepository));
                services.AddTransient(typeof(ProductModule.Domain.Interface.IRepository<OrderedProduct>), typeof(OrderedProductRepository));
                services.AddTransient(typeof(IOrderRepository<Order>), typeof(OrderRepository));
                services.AddTransient(typeof(IProductRepository<Product>), typeof(ProductRepository));


                #endregion

                #region Services
                services.AddScoped(typeof(IOrderService<Order>), typeof(OrderService));
                services.AddScoped(typeof(IOrderReportGeneratorService), typeof(OrderReportGeneratorService));


                #endregion

                services.AddScoped(typeof(Domain.Interface.IUnitOfWork<OrderModuleDbContext>), typeof(Domain.Repositories.UnitOfWork<OrderModuleDbContext>));

                #region RabbitMQ
                services.Configure<RabbitMQSettings>(opt => configuration.GetSection("RabbitMQSettings").Bind(opt));
                services.Configure<OrderChannelSettings>(opt => configuration.GetSection("OrderChannelSettings").Bind(opt));
                services.AddSingleton<IOrderRabbitMQManager, OrderRabbitMQManager>();

                #endregion

                #region SingnalR
                services.AddSignalR();
                #endregion

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
                Log.Logger.Error($"Error at DI IoC Order: {ex.Message}", ex);
            }

        }
    }
}