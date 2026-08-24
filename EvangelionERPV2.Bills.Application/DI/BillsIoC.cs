using Amazon.SecretsManager;
using EvangelionERPV2.BillsModule.Application.Configs;
using EvangelionERPV2.BillsModule.Application.Interface;
using EvangelionERPV2.BillsModule.Application.Services;
using EvangelionERPV2.BillsModule.Domain.Interface;
using EvangelionERPV2.BillsModule.Domain.Repositories;
using EvangelionERPV2.Shared.Configs;
using EvangelionERPV2.Shared.Context;
using EvangelionERPV2.Shared.Entities;
using EvangelionERPV2.Shared.Utils;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Serilog;

namespace EvangelionERPV2.BillsModule.Application.DI
{
    public class BillsIoC
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
                    var encryptedConnection = configuration.GetConnectionString("DefaultConnection") ?? string.Empty;
                    options.UseSqlServer(kmsProvider.GetKMSKey(encryptedConnection));
                });
                #endregion

                #region Mapper
                var mapper = MapperConfig.RegisterMaps().CreateMapper();
                services.AddSingleton(mapper);
                services.AddAutoMapper(config => { }, AppDomain.CurrentDomain.GetAssemblies());
                #endregion

                services.Configure<BillsSettings>(configuration.GetSection("BillsSettings"));
                services.Configure<DunningSettings>(configuration.GetSection("DunningSettings"));
                #region Repositorys
                services.AddTransient(typeof(EvangelionERPV2.Shared.Repositories.IRepository<>), typeof(EvangelionERPV2.Shared.Repositories.Repository<>));
                services.AddTransient(typeof(EvangelionERPV2.Shared.Repositories.IRepository<Bill>), typeof(BillsRepository));
                services.AddTransient(typeof(IBillsRepository<Bill>), typeof(BillsRepository));
                #endregion

                #region Services
                services.AddScoped<Func<EmailModule.Application.Interface.IEmailService<EmailStructure>>>(
                    sp => () => sp.GetRequiredService<EmailModule.Application.Interface.IEmailService<EmailStructure>>());
                services.AddScoped(typeof(IBillsService<Bill>), typeof(BillsService));
                services.AddScoped<IPayableBillService, PayableBillService>();
                services.AddScoped<IPayrollService, PayrollService>();
                services.AddScoped<IPayableBillReportGeneratorService, PayableBillReportGeneratorService>();
                services.AddScoped<ICashFlowForecastService, CashFlowForecastService>();
                services.AddScoped<IBankReconciliationService, BankReconciliationService>();
                services.AddScoped<IDailyPanelService, DailyPanelService>();
                services.AddScoped<IReceivablesReportService, ReceivablesReportService>();
                services.AddScoped<IAccountingExportService, AccountingExportService>();

                // Concrete, no interface: the worker is its only caller, and chasing late payers is
                // a consequence of the schedule rather than a capability anything else asks for.
                services.AddScoped<DunningService>();
                services.AddHostedService<DailySweepWorker>();

                // Concrete, no interface: férias pay is its only caller, and the drawdown is a
                // consequence of creating that bill rather than a capability anything else asks for.
                services.AddScoped<VacationProvisionDrawdownService>();
                services.AddScoped<IVacationPayService, VacationPayService>();
                #endregion

                services.AddScoped(typeof(EvangelionERPV2.Shared.Repositories.IUnitOfWork<AppDbContext>), typeof(EvangelionERPV2.Shared.Repositories.UnitOfWork<AppDbContext>));
            }
            catch (Exception ex)
            {
                Log.Logger.Error(
                    "Error at DI IoC Bills. ErrorType={ErrorType}",
                    ex.GetType().Name);
            }
        }
    }
}

