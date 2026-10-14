using EvangelionERPV2.OrderModule.Application.Interface;
using EvangelionERPV2.Shared.Entities;
using EvangelionERPV2.Shared.Enums;
using Microsoft.EntityFrameworkCore;
using Moq;

namespace EvangelionERPV2.StorefrontModule.Test
{
    /// <summary>
    /// What <c>OrderService</c> does that the storefront relies on — saves the order with its lines,
    /// adds shipping to the total, takes the stock (refusing to go below zero) and stamps status dates —
    /// against the test database. The real service needs SQL Server and a dozen collaborators.
    /// </summary>
    internal static class FakeOrderService
    {
        public static Mock<IOrderService<Order>> Create(StorefrontTestContext database)
        {
            var mock = new Mock<IOrderService<Order>>();

            mock.Setup(s => s.CreateAsync(It.IsAny<Order>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<string?>()))
                .Returns(async (Order order, bool _, bool allowNegativeStock, string? _) =>
                {
                    using var context = database.CreateContext();
                    database.CreateCalls++;
                    var failThisCall = database.FailOnCall == database.CreateCalls;
                    if (database.FailCreates > 0 || failThisCall)
                    {
                        if (failThisCall)
                            database.FailOnCall = 0;
                        else
                            database.FailCreates--;
                        database.DuringFailure?.Invoke(context);
                        await context.SaveChangesAsync();
                        throw new TimeoutException("Execution Timeout Expired.");
                    }

                    order.Id = Guid.NewGuid();
                    var lines = order.OrderedProduct!.ToList();
                    foreach (var line in lines)
                    {
                        var product = await context.Product.FirstAsync(p => p.Id == line.ProductId);
                        if (!allowNegativeStock && !product.IsService && product.StorageQuantity < line.Quantity)
                            throw new InvalidOperationException("Insufficient stock");
                        product.StorageQuantity -= line.Quantity;
                        line.Id = Guid.NewGuid();
                        line.OrderId = order.Id;
                    }

                    order.TotalValue = lines.Sum(l => l.Value * l.Quantity) + order.ShippingValue;
                    order.IsActive = true;
                    order.OrderedProduct = null;
                    context.Order.Add(order);
                    context.OrderedProduct.AddRange(lines);
                    await context.SaveChangesAsync();
                    order.OrderedProduct = lines;
                    return order;
                });

            mock.Setup(s => s.Update(It.IsAny<Order>(), It.IsAny<Guid>()))
                .Returns((Order order, Guid enterpriseId) =>
                {
                    using var context = database.CreateContext();
                    var existent = context.Order.First(o => o.Id == order.Id && o.EnterpriseId == enterpriseId);
                    existent.Status = order.Status;
                    if (order.Status == (int)EnumOrderStatus.Shipped) existent.ShippedAt ??= DateTime.UtcNow;
                    if (order.Status is (int)EnumOrderStatus.Delivered or (int)EnumOrderStatus.Finished) existent.DeliveredAt ??= DateTime.UtcNow;
                    if (order.TrackingCode is not null) existent.TrackingCode = order.TrackingCode;
                    if (order.TrackingUrl is not null) existent.TrackingUrl = order.TrackingUrl;
                    context.SaveChanges();
                    return existent;
                });

            return mock;
        }
    }
}
