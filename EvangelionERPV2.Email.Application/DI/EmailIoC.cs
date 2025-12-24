using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.EntityFrameworkCore;
using Serilog;
using EvangelionERPV2.Shared.Entities.RabbitMQ;
using EvangelionERPV2.Shared.Configs;
using EvangelionERPV2.Shared.Entities;
using EvangelionERPV2.EnterpriseModule.Domain.Interface;
using EvangelionERPV2.EmailModule.Application.Services;
using EvangelionERPV2.EmailModule.Application.Interface;
using EvangelionERPV2.OrderModule.Application.Interface;
using EvangelionERPV2.OrderModule.Application.Services;
using EvangelionERPV2.Shared.Utils;
using Amazon.SecretsManager;
using EvangelionERPV2.EmailModule.Domain.Repositories;
using EvangelionERPV2.EmailModule.Infra.Context;
using EvangelionERPV2.EnterpriseModule.Infra.Context;

namespace EvangelionERPV2.EmailModule.Application.DI
{
    public class EmailIoC
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
                services.AddDbContext<EmailModuleDbContext>(options => options.UseSqlServer(kmsProvider.GetKMSKey(configuration.GetConnectionString("DefaultConnection") ?? string.Empty)));
                #endregion

                #region Mapper
                var mapper = MapperConfig.RegisterMaps().CreateMapper();
                services.AddSingleton(mapper);
                services.AddAutoMapper(config => { }, AppDomain.CurrentDomain.GetAssemblies());

                #endregion

                #region Repositorys
                services.AddTransient(typeof(IRepository<Enterprise>), typeof(EnterpriseModule.Domain.Repositories.EnterpriseRepository));
                services.AddTransient(typeof(Domain.Interface.IRepository<Email>), typeof(Domain.Repositories.Repository<Email>));


                #endregion

                #region Services
                services.AddScoped(typeof(IEmailService<EmailStructure>), typeof(EmailService));
                services.AddScoped(typeof(IOrderService<Order>), typeof(OrderService));
                services.AddScoped(typeof(IOrderReportGeneratorService), typeof(OrderReportGeneratorService));


                #endregion

                services.AddScoped(typeof(Domain.Interface.IUnitOfWork<EmailModuleDbContext>), typeof(UnitOfWork<EmailModuleDbContext>));

                #region RabbitMQ
                services.Configure<RabbitMQSettings>(opt => configuration.GetSection("RabbitMQSettings").Bind(opt));
                services.Configure<EmailChannelSettings>(opt => configuration.GetSection("EmailChannelSettings").Bind(opt));
                services.AddSingleton<IEmailRabbitMQManager, EmailRabbitMQManager>();

                #endregion
            }
            catch (Exception ex)
            {
                Log.Logger.Error($"Error at DI IoC Email: {ex.Message}", ex);
            }

        }
    }
}