using EvangelionERPV2.ProductModule.Application.Interface;
using EvangelionERPV2.Shared.Entities;
using EvangelionERPV2.Shared.Utils;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;

namespace EvangelionERPV2.ProductModule.Application.Services
{
    /// <summary>
    /// Runs <see cref="ProductService.BackfillPicturesAsync"/> once at startup on whichever replica takes
    /// the lease. Registered only while <c>Products:BackfillPictures</c> is true.
    /// </summary>
    public sealed class ProductPictureBackfillWorker(IServiceScopeFactory scopes, IDistributedLease lease) : BackgroundService
    {
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            try
            {
                if (await lease.TryAcquireAsync("eva:lease:product-picture-backfill", TimeSpan.FromHours(1)) == null)
                    return;

                using var scope = scopes.CreateScope();
                await scope.ServiceProvider.GetRequiredService<IProductService<Product>>().BackfillPicturesAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                Log.Logger.Error(ex, "Product picture backfill failed.");
            }
        }
    }
}
