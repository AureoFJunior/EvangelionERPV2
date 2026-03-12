using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.EntityFrameworkCore;
using EvangelionERPV2.Infra.Context;
using EvangelionERPV2.Application.Configs;
using EvangelionERPV2.Domain.Interfaces;
using EvangelionERPV2.Infra.Repositories;
using EvangelionERPV2.Domain.Utils;
using Serilog;
using EvangelionERPV2.Domain.Models;
using EvangelionERPV2.Domain.Interfaces.Services;
using EvangelionERPV2.Domain.Interfaces.Repositories;
using EvangelionERPV2.Domain.Models.Token;
using EvangelionERPV2.Domain.Models.RabbitMQ;
using Amazon.SecretsManager;

namespace EvangelionERPV2.Application.DI
{
    public class IoC
    {
        public static void Configure(IServiceCollection services, string connection, IConfiguration configuration)
        {
            try
            {
                services.AddLogging();

                #region DataBase
                services.AddDbContextPool<AppDbContext>(options => options.UseSqlServer(connection));

                #endregion

                #region Mapper
                var mapper = MapperConfig.RegisterMaps().CreateMapper();
                services.AddSingleton(mapper);
                services.AddAutoMapper(AppDomain.CurrentDomain.GetAssemblies());

                #endregion

                #region Repositorys
                services.AddTransient(typeof(EvangelionERPV2.Shared.Repositories.IRepository<>), typeof(EvangelionERPV2.Shared.Repositories.Repository<>));
                services.AddTransient(typeof(EvangelionERPV2.Shared.Repositories.IRepository<User>), typeof(UserRepository));
                services.AddTransient(typeof(EvangelionERPV2.Shared.Repositories.IRepository<Enterprise>), typeof(EnterpriseRepository));
                services.AddTransient(typeof(EvangelionERPV2.Shared.Repositories.IRepository<Customer>), typeof(CustomerRepository));
                services.AddTransient(typeof(EvangelionERPV2.Shared.Repositories.IRepository<Product>), typeof(ProductRepository));
                services.AddTransient(typeof(EvangelionERPV2.Shared.Repositories.IRepository<Order>), typeof(OrderRepository));
                services.AddTransient(typeof(EvangelionERPV2.Shared.Repositories.IRepository<OrderedProduct>), typeof(OrderedProductRepository));
                services.AddTransient(typeof(ICustomerRepository<Customer>), typeof(CustomerRepository));
                services.AddTransient(typeof(IEnterpriseRepository<Enterprise>), typeof(EnterpriseRepository));
                services.AddTransient(typeof(IOrderRepository<Order>), typeof(OrderRepository));
                services.AddTransient(typeof(IProductRepository<Product>), typeof(ProductRepository));


                #endregion

                #region Services
                services.AddTransient(typeof(TokenService));
                services.AddTransient(typeof(IUserService<User>), typeof(UserService));
                services.AddTransient(typeof(IEnterpriseService<Enterprise>), typeof(EnterpriseService));
                services.AddTransient(typeof(ICustomerService<Customer>), typeof(CustomerService));
                services.AddTransient(typeof(IProductService<Product>), typeof(ProductService));
                services.AddTransient(typeof(IOrderService<Order>), typeof(OrderService));
                services.AddTransient(typeof(IOrderedProductService<OrderedProduct>), typeof(OrderedProductService));
                services.AddTransient(typeof(IEmailService<Email>), typeof(EmailService));


                #endregion

                services.AddScoped(typeof(EvangelionERPV2.Shared.Repositories.IUnitOfWork<AppDbContext>), typeof(EvangelionERPV2.Shared.Repositories.UnitOfWork<AppDbContext>));

                #region RabbitMQ
                services.Configure<RabbitMQSettings>(opt => configuration.GetSection("RabbitMQSettings").Bind(opt));
                services.Configure<EmailSettings>(opt => configuration.GetSection("EmailSettings").Bind(opt));
                services.Configure<OrderChannelSettings>(opt => configuration.GetSection("OrderChannelSettings").Bind(opt));
                services.Configure<EmailChannelSettings>(opt => configuration.GetSection("EmailChannelSettings").Bind(opt));
                services.Configure<BaseChannelSettings>(opt => configuration.GetSection("BaseChannelSettings").Bind(opt));
                services.AddSingleton(typeof(IRabbitMQManager), typeof(RabbitMQManager));

                #endregion

                #region Settings
                services.Configure<EmailSettings>(opt => configuration.GetSection("EmailSettings").Bind(opt));
                #endregion

                #region AWS
                services.AddSingleton<IAmazonSecretsManager, AmazonSecretsManagerClient>(sp =>
                {
                    var region = Amazon.RegionEndpoint.USEast1;
                    return new AmazonSecretsManagerClient(region);
                });
                services.AddTransient<AWSCredentialsProvider>(sp =>
                {
                    var secretsManager = sp.GetRequiredService<IAmazonSecretsManager>();
                    return new AWSCredentialsProvider(secretsManager, configuration);
                });

                #endregion

                #region SingnalR
                services.AddSignalR();
                #endregion

                #region Redis
                services.AddStackExchangeRedisCache(o =>
                {
                    o.InstanceName = "evaRedis";
                    o.Configuration = configuration.GetSection("RedisSettings")["ConnectionString"];
                });
                #endregion
            }
            catch (Exception ex)
            {
                Log.Logger.Error(ex, $"Error at DI IoC: {ex.Message}");
            }

        }
    }
}
