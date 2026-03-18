using EvangelionERPV2.OrderModule.Application.Services;
using EvangelionERPV2.ProductModule.Application.DI;
using EvangelionERPV2.Shared.Entities;
using EvangelionERPV2.Shared.Enums;
using EvangelionERPV2.Shared.Exceptions;
using EvangelionERPV2.Shared.Hubs;
using Microsoft.AspNetCore.SignalR;
using Moq;
using System.Reflection;

namespace EvangelionERPV2.OrderModule.Test.Bills
{
    public class OrdersTest
    {
        private readonly Mock<EvangelionERPV2.Shared.Repositories.IRepository<Order>> _mockIOrderRepository;
        private readonly Mock<OrderModule.Domain.Interface.IOrderRepository<Order>> _mockIOrderRepositoryCustom;
        private readonly Mock<EvangelionERPV2.Shared.Repositories.IRepository<Product>> _mockIProductRepository;
        private readonly Mock<EvangelionERPV2.Shared.Repositories.IRepository<OrderedProduct>> _mockIOrderedProductRepository;
        private readonly Mock<EvangelionERPV2.Shared.Repositories.IRepository<Enterprise>> _mockIEnterpriseRepository;
        private readonly Mock<ProductModule.Application.Interface.IProductService<Product>> _mockIProductService;
        private readonly OrderService _orderService;
        private readonly OrderReportGeneratorService _orderReportGeneratorService;

        public OrdersTest()
        {
            _mockIOrderRepository = new Mock<EvangelionERPV2.Shared.Repositories.IRepository<Order>>();
            _mockIOrderRepositoryCustom = new Mock<OrderModule.Domain.Interface.IOrderRepository<Order>>();
            _mockIProductRepository = new Mock<EvangelionERPV2.Shared.Repositories.IRepository<Product>>();
            _mockIOrderedProductRepository = new Mock<EvangelionERPV2.Shared.Repositories.IRepository<OrderedProduct>>();
            _mockIEnterpriseRepository = new Mock<EvangelionERPV2.Shared.Repositories.IRepository<Enterprise>>();
            _mockIProductService = new Mock<ProductModule.Application.Interface.IProductService<Product>>();
            SetupTransactionalExecution(_mockIOrderRepository);
            _mockIEnterpriseRepository
                .Setup(r => r.GetById(It.IsAny<Guid>()))
                .Returns((Guid enterpriseId) => new Enterprise { Id = enterpriseId, IsActive = true, Name = "Enterprise", CurrentBalance = 0 });
            _mockIEnterpriseRepository
                .Setup(r => r.GetByIdAsync(It.IsAny<Guid>()))
                .ReturnsAsync((Guid enterpriseId) => new Enterprise { Id = enterpriseId, IsActive = true, Name = "Enterprise", CurrentBalance = 0 });

            _orderReportGeneratorService = new OrderReportGeneratorService(_mockIProductRepository.Object);

            // Build OrderService with mocked parameters
            _orderService = new OrderService(_mockIOrderRepository.Object,
                _mockIOrderRepositoryCustom.Object,
                _mockIProductRepository.Object,
                _mockIOrderedProductRepository.Object,
                _mockIEnterpriseRepository.Object,
                _mockIProductService.Object,
                null,
                _orderReportGeneratorService,
                null);
        }


        #region Unit Tests

        [Theory]
        [MemberData(nameof(GetOrdersWithNegativeValues))]
        public async Task OrderService_Order_Should_Not_Have_Negative_Or_Null_Values_Otherwise_Error(Order order)
        {
            try
            {
                // Arrange

                // Act
                var result = await _orderService.CreateAsync(order);

                // Assert
                Assert.Null(result);
            }
            catch (InsertDatabaseException ex)
            {
                // Negative or Null values Identified
                Assert.True(true);
            }
        }

        [Fact]
        public async Task CreateAsync_ValidOrder_ReturnsOrder()
        {
            // Arrange
            var order = new Order(DateTime.Now, DateTime.Now.AddDays(1), 100, Guid.NewGuid(), Guid.NewGuid(),
                new List<OrderedProduct>
                {
                    new OrderedProduct
                    {
                        CreatedAt = DateTime.Now,
                        UpdatedAt = DateTime.Now,
                        IsActive = true,
                        Quantity = 2,
                        Value = 50,
                        ProductId = Guid.NewGuid()
                    }
                }, Guid.NewGuid());

            _mockIOrderRepository.Setup(r => r.CreateAsync(It.IsAny<Order>())).ReturnsAsync(order);

            // Act
            var result = await _orderService.CreateAsync(order);

            // Assert
            Assert.NotNull(result);
            Assert.Equal(order, result);
        }

        [Fact]
        public async Task CreateAsync_IgnoresClientProvidedIds_GeneratesServerIds()
        {
            // Arrange
            var incomingOrderId = Guid.NewGuid();
            var incomingLineId = Guid.NewGuid();
            var incomingLineOrderId = Guid.NewGuid();

            var order = new Order(DateTime.Now, DateTime.Now.AddDays(1), 100, Guid.NewGuid(), Guid.NewGuid(),
                new List<OrderedProduct>
                {
                    new OrderedProduct
                    {
                        Id = incomingLineId,
                        OrderId = incomingLineOrderId,
                        CreatedAt = DateTime.Now,
                        UpdatedAt = DateTime.Now,
                        IsActive = true,
                        Quantity = 2,
                        Value = 50,
                        ProductId = Guid.NewGuid()
                    }
                }, Guid.NewGuid())
            {
                Id = incomingOrderId
            };

            Order? createdOrder = null;
            IEnumerable<OrderedProduct>? createdLines = null;

            _mockIOrderRepository.Setup(r => r.CreateAsync(It.IsAny<Order>()))
                .Callback<Order>(entity => createdOrder = entity)
                .ReturnsAsync((Order entity) => entity);

            _mockIOrderedProductRepository.Setup(r => r.CreateRangeAsync(It.IsAny<IEnumerable<OrderedProduct>>()))
                .Callback<IEnumerable<OrderedProduct>>(items => createdLines = items.ToList())
                .ReturnsAsync((IEnumerable<OrderedProduct> items) => items);

            _mockIOrderRepository.Setup(r => r.CommitAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
            _mockIOrderedProductRepository.Setup(r => r.CommitAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
            _mockIProductService.Setup(p => p.UpdateForOrder(It.IsAny<Order>())).Returns(Task.CompletedTask);

            // Act
            var result = await _orderService.CreateAsync(order);

            // Assert
            Assert.NotNull(createdOrder);
            Assert.NotEqual(incomingOrderId, result.Id);
            Assert.Equal(result.Id, createdOrder?.Id);

            var line = createdLines?.SingleOrDefault();
            Assert.NotNull(line);
            Assert.NotEqual(incomingLineId, line?.Id ?? Guid.Empty);
            Assert.Equal(result.Id, line?.OrderId);
        }

        [Theory]
        [MemberData(nameof(GetInvalidOrders))]
        public async Task CreateAsync_InvalidOrder_ThrowsInsertDatabaseException(Order invalidOrder)
        {
            // Act & Assert
            await Assert.ThrowsAsync<InsertDatabaseException>(() => _orderService.CreateAsync(invalidOrder));
        }

        public static IEnumerable<object[]> GetInvalidOrders()
        {
            // Negative quantity
            yield return new object[] { new Order(DateTime.Now, DateTime.Now.AddDays(1), 100, Guid.NewGuid(), Guid.NewGuid(),
                new List<OrderedProduct>
                {
                    new OrderedProduct
                    {
                        CreatedAt = DateTime.Now,
                        UpdatedAt = DateTime.Now,
                        IsActive = true,
                        Quantity = -1,
                        Value = 50,
                        ProductId = Guid.NewGuid()
                    }
                }, Guid.NewGuid())};

            // Zero value
            yield return new object[] { new Order(DateTime.Now, DateTime.Now.AddDays(1), 0, Guid.NewGuid(), Guid.NewGuid(),
                new List<OrderedProduct>
                {
                    new OrderedProduct
                    {
                        CreatedAt = DateTime.Now,
                        UpdatedAt = DateTime.Now,
                        IsActive = true,
                        Quantity = 1,
                        Value = 0,
                        ProductId = Guid.NewGuid()
                    }
                }, Guid.NewGuid())};
        }

        [Fact]
        public void VerifyValidValues_ValidOrder_DoesNotThrow()
        {
            // Arrange
            var order = new Order(DateTime.Now, DateTime.Now.AddDays(1), 100, Guid.NewGuid(), Guid.NewGuid(),
                new List<OrderedProduct>
                {
                    new OrderedProduct
                    {
                        CreatedAt = DateTime.Now,
                        UpdatedAt = DateTime.Now,
                        IsActive = true,
                        Quantity = 1,
                        Value = 100,
                        ProductId = Guid.NewGuid()
                    }
                }, Guid.NewGuid());

            // Act & Assert
            var refOrder = order;
            _orderService.VerifyValidValues(ref refOrder);
        }

        [Fact]
        public void Update_ValidOrder_ReturnsUpdatedOrder()
        {
            // Arrange
            var orderId = Guid.NewGuid();
            var originalOrder = new Order(DateTime.Now, DateTime.Now.AddDays(1), 100, Guid.NewGuid(), Guid.NewGuid(),
                new List<OrderedProduct>
                {
            new OrderedProduct
            {
                CreatedAt = DateTime.Now,
                UpdatedAt = DateTime.Now,
                IsActive = true,
                Quantity = 1,
                Value = 100,
                ProductId = Guid.NewGuid()
            }
                }, Guid.NewGuid());

            // Simulate the order already exists in the repository
            _mockIOrderRepository.Setup(r => r.GetById(orderId)).Returns(originalOrder);

            // Simulate updating the order
            var updatedOrder = new Order(originalOrder.Payday, originalOrder.PaymentScheduledDate, 200, originalOrder.EnterpriseId, originalOrder.CustomerId, originalOrder.OrderedProduct, Guid.NewGuid())
            {
                // Set the same ID as the original
                Id = orderId
            };

            _mockIOrderRepository.Setup(r => r.Update(It.IsAny<Order>())).Returns(updatedOrder);

            // Act
            var result = _orderService.Update(updatedOrder);

            // Assert
            Assert.NotNull(result);
            Assert.Equal(200, result.TotalValue);
            Assert.Equal(orderId, result.Id);
        }

        [Fact]
        public void Update_WhenOrderIsFinished_ShouldThrowInsertDatabaseException()
        {
            var orderId = Guid.NewGuid();
            var existentOrder = new Order
            {
                Id = orderId,
                Status = (int)EnumOrderStatus.Finished,
                IsActive = true
            };

            var payload = new Order
            {
                Id = orderId,
                Status = (int)EnumOrderStatus.Delivered,
                TotalValue = 100
            };

            _mockIOrderRepository.Setup(r => r.GetById(orderId)).Returns(existentOrder);

            Assert.Throws<InsertDatabaseException>(() => _orderService.Update(payload));
        }

        [Fact]
        public async Task RefundAsync_ValidOrder_ShouldRestoreStockAndSetRefundState()
        {
            var enterpriseId = Guid.NewGuid();
            var orderId = Guid.NewGuid();
            var productId = Guid.NewGuid();

            var existentOrder = new Order
            {
                Id = orderId,
                EnterpriseId = enterpriseId,
                IsActive = true,
                Status = (int)EnumOrderStatus.Processing,
                TotalValue = 80
            };

            var orderedProducts = new List<OrderedProduct>
            {
                new()
                {
                    Id = Guid.NewGuid(),
                    OrderId = orderId,
                    ProductId = productId,
                    Quantity = 4,
                    Value = 20,
                    IsActive = true
                }
            };

            var products = new List<Product>
            {
                new()
                {
                    Id = productId,
                    EnterpriseId = enterpriseId,
                    StorageQuantity = 6,
                    IsActive = true,
                    Name = "Mouse"
                }
            };

            _mockIOrderRepository.Setup(r => r.GetByIdAsync(orderId)).ReturnsAsync(existentOrder);
            _mockIOrderRepository.Setup(r => r.CommitAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

            _mockIOrderedProductRepository
                .Setup(r => r.GetAllAsync(It.IsAny<Func<OrderedProduct, bool>?>()))
                .ReturnsAsync((Func<OrderedProduct, bool>? predicate) =>
                {
                    IEnumerable<OrderedProduct> query = orderedProducts;
                    if (predicate != null)
                        query = query.Where(predicate);
                    return query;
                });

            _mockIProductRepository
                .Setup(r => r.GetAllAsync(It.IsAny<Func<Product, bool>?>()))
                .ReturnsAsync((Func<Product, bool>? predicate) =>
                {
                    IEnumerable<Product> query = products;
                    if (predicate != null)
                        query = query.Where(predicate);
                    return query;
                });

            var refunded = await _orderService.RefundAsync(orderId, enterpriseId, "Customer returned wrong item");

            Assert.Equal((int)EnumOrderStatus.Refund, refunded.Status);
            Assert.Equal(0, refunded.TotalValue);
            Assert.Equal("Customer returned wrong item", refunded.RefundReason);
            Assert.NotNull(refunded.RefundedAt);
            Assert.Equal(10, products[0].StorageQuantity);
            Assert.Equal(0, orderedProducts[0].Quantity);
            Assert.Equal(0, orderedProducts[0].Value);

            _mockIProductRepository.Verify(r => r.UpdateRange(It.IsAny<IEnumerable<Product>>()), Times.Once);
            _mockIOrderedProductRepository.Verify(r => r.UpdateRange(It.IsAny<IEnumerable<OrderedProduct>>()), Times.Once);
            _mockIOrderRepository.Verify(r => r.Update(It.IsAny<Order>()), Times.Once);
        }

        [Fact]
        public async Task RefundAsync_WhenReasonIsEmpty_ShouldThrowInsertDatabaseException()
        {
            await Assert.ThrowsAsync<InsertDatabaseException>(
                () => _orderService.RefundAsync(Guid.NewGuid(), Guid.NewGuid(), " "));
        }

        [Fact]
        public async Task RefundAsync_WhenOrderIsFinished_ShouldThrowInsertDatabaseException()
        {
            var enterpriseId = Guid.NewGuid();
            var order = new Order
            {
                Id = Guid.NewGuid(),
                EnterpriseId = enterpriseId,
                IsActive = true,
                Status = (int)EnumOrderStatus.Finished
            };

            _mockIOrderRepository.Setup(r => r.GetByIdAsync(order.Id)).ReturnsAsync(order);

            await Assert.ThrowsAsync<InsertDatabaseException>(
                () => _orderService.RefundAsync(order.Id, enterpriseId, "Cannot change final status"));
        }

        #endregion

        #region Edge Cases
        // Order with No OrderedProducts
        [Fact]
        public async Task CreateAsync_OrderWithNoOrderedProducts_ThrowsInsertDatabaseException()
        {
            var order = new Order(DateTime.Now, DateTime.Now.AddDays(1), 100, Guid.NewGuid(), Guid.NewGuid(), new List<OrderedProduct>(), Guid.NewGuid());
            await Assert.ThrowsAsync<InsertDatabaseException>(() => _orderService.CreateAsync(order));
        }

        // Order with Null OrderedProducts
        [Fact]
        public async Task CreateAsync_OrderWithNullOrderedProducts_ThrowsInsertDatabaseException()
        {
            var order = new Order(DateTime.Now, DateTime.Now.AddDays(1), 100, Guid.NewGuid(), Guid.NewGuid(), null, Guid.NewGuid());
            await Assert.ThrowsAsync<InsertDatabaseException>(() => _orderService.CreateAsync(order));
        }

        [Fact]
        public async Task CreateAsync_OrderWithDuplicateProducts_ThrowsInsertDatabaseException()
        {
            var productId = Guid.NewGuid();
            var order = new Order(DateTime.Now, DateTime.Now.AddDays(1), 100, Guid.NewGuid(), Guid.NewGuid(),
                new List<OrderedProduct>
                {
            new OrderedProduct { CreatedAt = DateTime.Now, UpdatedAt = DateTime.Now, IsActive = true, Quantity = 1, Value = 50, ProductId = productId },
            new OrderedProduct { CreatedAt = DateTime.Now, UpdatedAt = DateTime.Now, IsActive = true, Quantity = 2, Value = 50, ProductId = productId }
                }, Guid.NewGuid());
            await Assert.ThrowsAsync<InsertDatabaseException>(() => _orderService.CreateAsync(order));
        }

        // Order with Extremely Large Quantities/Values
        [Fact]
        public async Task CreateAsync_OrderWithLargeQuantityOrValue_ThrowsInsertDatabaseException()
        {
            var order = new Order(DateTime.Now, DateTime.Now.AddDays(1), double.MaxValue, Guid.NewGuid(), Guid.NewGuid(),
                new List<OrderedProduct>
                {
            new OrderedProduct { CreatedAt = DateTime.Now, UpdatedAt = DateTime.Now, IsActive = true, Quantity = double.MaxValue, Value = double.MaxValue, ProductId = Guid.NewGuid() }
                }, Guid.NewGuid());
            await Assert.ThrowsAsync<InsertDatabaseException>(() => _orderService.CreateAsync(order));
        }

        // Order Update with Nonexistent Order
        [Fact]
        public void Update_NonexistentOrder_ThrowsException()
        {
            var orderId = Guid.NewGuid();
            var order = new Order(DateTime.Now, DateTime.Now.AddDays(1), 100, Guid.NewGuid(), Guid.NewGuid(),
                new List<OrderedProduct>
                {
            new OrderedProduct { CreatedAt = DateTime.Now, UpdatedAt = DateTime.Now, IsActive = true, Quantity = 1, Value = 100, ProductId = Guid.NewGuid() }
                }, Guid.NewGuid())
            { Id = orderId };

            _mockIOrderRepository.Setup(r => r.GetById(orderId)).Returns((Order)null);

            Assert.Throws<NotFoundDatabaseException>(() => _orderService.Update(order));
        }

        [Fact]
        public void Delete_WhenOrderIsRefund_ShouldThrowInsertDatabaseException()
        {
            var orderId = Guid.NewGuid();
            var order = new Order
            {
                Id = orderId,
                Status = (int)EnumOrderStatus.Refund,
                IsActive = true
            };

            _mockIOrderRepository.Setup(r => r.GetById(orderId)).Returns(order);

            Assert.Throws<InsertDatabaseException>(() => _orderService.Delete(orderId));
        }
        #endregion

        #region Data Pull

        public static IEnumerable<object[]> GetOrdersWithNegativeValues()
        {
            // Case 01 - Negative Product Quantity
            yield return new object[] { new Order(null, DateTime.Now.AddMonths(2), 10, Guid.NewGuid(), Guid.NewGuid(),
                new List<OrderedProduct>
                {
                    new OrderedProduct
                    {
                        CreatedAt = DateTime.Now,
                        UpdatedAt = DateTime.Now,
                        IsActive = true,
                        Quantity = -1,
                        Value = 10,
                        ProductId = Guid.NewGuid()
                    }
                }, Guid.NewGuid())
            };

            // Case 02 - Negative Total Value
            yield return new object[] { new Order(null, DateTime.Now.AddMonths(2), 10, Guid.NewGuid(), Guid.NewGuid(),
                new List<OrderedProduct>
                {
                    new OrderedProduct
                    {
                        CreatedAt = DateTime.Now,
                        UpdatedAt = DateTime.Now,
                        IsActive = true,
                        Quantity = 1,
                        Value = -10,
                        ProductId = Guid.NewGuid()
                    }
                }, Guid.NewGuid())
            };

            // Case 03 - Negative Product Quantity and Negative Total Value
            yield return new object[] { new Order(null, DateTime.Now.AddMonths(2), 10, Guid.NewGuid(), Guid.NewGuid(),
                new List<OrderedProduct>
                {
                    new OrderedProduct
                    {
                        CreatedAt = DateTime.Now,
                        UpdatedAt = DateTime.Now,
                        IsActive = true,
                        Quantity = -1,
                        Value = -10,
                        ProductId = Guid.NewGuid()
                    }
                }, Guid.NewGuid())
            };

            // Case 04 - Empty Quantity
            yield return new object[] { new Order(null, DateTime.Now.AddMonths(2), 10, Guid.NewGuid(), Guid.NewGuid(),
                new List<OrderedProduct>
                {
                    new OrderedProduct
                    {
                        CreatedAt = DateTime.Now,
                        UpdatedAt = DateTime.Now,
                        IsActive = true,
                        Quantity = 0,
                        Value = 10,
                        ProductId = Guid.NewGuid()
                    }
                }, Guid.NewGuid())
            };

            // Case 05 - Empty Value
            yield return new object[] { new Order(null, DateTime.Now.AddMonths(2), 10, Guid.NewGuid(), Guid.NewGuid(),
                new List<OrderedProduct>
                {
                    new OrderedProduct
                    {
                        CreatedAt = DateTime.Now,
                        UpdatedAt = DateTime.Now,
                        IsActive = true,
                        Quantity = 2,
                        Value = 0,
                        ProductId = Guid.NewGuid()
                    }
                }, Guid.NewGuid())
            };
        }

        #endregion

        #region SignalR
        [Fact]
        public async Task SendOrderUpdate_CallsClientsAllSendAsync()
        {
            // Arrange
            var hub = new OrderHub();

            var mockClients = new Mock<IHubCallerClients>();
            var mockClientProxy = new Mock<IClientProxy>();

            mockClients.Setup(c => c.All).Returns(mockClientProxy.Object);

            // Set the non-public setter for Hub.Clients via reflection
            var clientsProperty = typeof(Hub).GetProperty("Clients", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            var setMethod = clientsProperty.GetSetMethod(true);
            setMethod.Invoke(hub, new object[] { mockClients.Object });

            var orderId = Guid.NewGuid().ToString();
            var status = "Created";

            // Act
            await hub.SendOrderUpdate(orderId, status);

            // Assert
            mockClientProxy.Verify(
                p => p.SendCoreAsync(
                    "ReceiveOrderUpdate",
                    It.Is<object[]>(o => o != null && o.Length == 2 && (string)o[0] == orderId && (string)o[1] == status),
                    It.IsAny<CancellationToken>()),
                Times.Once);
        }

        [Fact]
        public async Task CreateAsync_SendsSignalRNotification()
        {
            // Arrange
            var order = new Order(DateTime.UtcNow, DateTime.UtcNow.AddDays(1), 100, Guid.NewGuid(), Guid.NewGuid(),
                new List<OrderedProduct>
                {
                    new OrderedProduct { CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow, IsActive = true, Quantity = 1, Value = 100, ProductId = Guid.NewGuid() }
                }, Guid.NewGuid());

            var mockOrderRepo = new Mock<EvangelionERPV2.Shared.Repositories.IRepository<Order>>();
            var mockOrderRepoCustom = new Mock<OrderModule.Domain.Interface.IOrderRepository<Order>>();
            var mockProductRepo = new Mock<EvangelionERPV2.Shared.Repositories.IRepository<Product>>();
            var mockOrderedProductRepo = new Mock<EvangelionERPV2.Shared.Repositories.IRepository<OrderedProduct>>();
            var mockEnterpriseRepo = new Mock<EvangelionERPV2.Shared.Repositories.IRepository<Enterprise>>();
            var mockProductService = new Mock<ProductModule.Application.Interface.IProductService<Product>>();
            SetupTransactionalExecution(mockOrderRepo);
            mockEnterpriseRepo
                .Setup(r => r.GetById(It.IsAny<Guid>()))
                .Returns((Guid enterpriseId) => new Enterprise { Id = enterpriseId, IsActive = true, Name = "Enterprise", CurrentBalance = 0 });
            mockEnterpriseRepo
                .Setup(r => r.GetByIdAsync(It.IsAny<Guid>()))
                .ReturnsAsync((Guid enterpriseId) => new Enterprise { Id = enterpriseId, IsActive = true, Name = "Enterprise", CurrentBalance = 0 });

            // CommitAsync must be setup to avoid awaiting null
            mockOrderRepo.Setup(r => r.CreateAsync(It.IsAny<Order>())).ReturnsAsync(order);
            mockOrderRepo.Setup(r => r.CommitAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
            mockOrderedProductRepo.Setup(r => r.CreateRangeAsync(It.IsAny<IEnumerable<OrderedProduct>>())).ReturnsAsync(order.OrderedProduct);
            mockOrderedProductRepo.Setup(r => r.CommitAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
            mockProductService.Setup(p => p.UpdateForOrder(It.IsAny<Order>())).Returns(Task.CompletedTask);

            // Setup SignalR mocks
            var mockHubContext = new Mock<IHubContext<OrderHub>>();
            var mockClients = new Mock<IHubClients>();
            var mockClientProxy = new Mock<IClientProxy>();

            mockHubContext.SetupGet(h => h.Clients).Returns(mockClients.Object);
            mockClients.Setup(c => c.All).Returns(mockClientProxy.Object);
            mockClientProxy.Setup(p => p.SendCoreAsync(It.IsAny<string>(), It.IsAny<object[]>(), It.IsAny<CancellationToken>()))
                           .Returns(Task.CompletedTask);

            var orderReportGeneratorService = new OrderReportGeneratorService(mockProductRepo.Object);

            var orderService = new OrderService(
                mockOrderRepo.Object,
                mockOrderRepoCustom.Object,
                mockProductRepo.Object,
                mockOrderedProductRepo.Object,
                mockEnterpriseRepo.Object,
                mockProductService.Object,
                null,
                orderReportGeneratorService,
                mockHubContext.Object);

            // Act
            var result = await orderService.CreateAsync(order);

            // Assert
            Assert.NotNull(result);
            mockClientProxy.Verify(
                p => p.SendCoreAsync(
                    "ReceiveOrderUpdate",
                    It.Is<object[]>(args => args.Length == 2 && args[0].ToString() == order.Id.ToString() && args[1].ToString() == "Created"),
                    It.IsAny<CancellationToken>()),
                Times.Once);
        }

        private static void SetupTransactionalExecution(Mock<EvangelionERPV2.Shared.Repositories.IRepository<Order>> repositoryMock)
        {
            repositoryMock
                .Setup(r => r.ExecuteInTransactionAsync(It.IsAny<Func<Task>>(), It.IsAny<CancellationToken>()))
                .Returns((Func<Task> operation, CancellationToken _) => operation());

            repositoryMock
                .Setup(r => r.ExecuteInTransaction(It.IsAny<Action>(), It.IsAny<CancellationToken>()))
                .Callback((Action operation, CancellationToken _) => operation());

            repositoryMock
                .Setup(r => r.ExecuteInTransaction(It.IsAny<Func<Order>>(), It.IsAny<CancellationToken>()))
                .Returns((Func<Order> operation, CancellationToken _) => operation());
        }
        #endregion
    }
}
