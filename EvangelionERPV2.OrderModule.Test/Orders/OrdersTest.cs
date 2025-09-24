using EvangelionERPV2.OrderModule.Application.Services;
using EvangelionERPV2.Shared.Entities;
using EvangelionERPV2.Shared.Exceptions;
using Moq;

namespace EvangelionERPV2.OrderModule.Test.Bills
{
    public class OrdersTest
    {
        private readonly Mock<OrderModule.Domain.Interface.IRepository<Order>> _mockIOrderRepository;
        private readonly Mock<OrderModule.Domain.Interface.IOrderRepository<Order>> _mockIOrderRepositoryCustom;
        private readonly Mock<OrderModule.Domain.Interface.IRepository<Product>> _mockIProductRepository;
        private readonly Mock<OrderModule.Domain.Interface.IRepository<OrderedProduct>> _mockIOrderedProductRepository;
        private readonly Mock<ProductModule.Application.Interface.IProductService<Product>> _mockIProductService;
        private readonly OrderService _orderService;

        public OrdersTest()
        {
            _mockIOrderRepository = new Mock<OrderModule.Domain.Interface.IRepository<Order>>();
            _mockIOrderRepositoryCustom = new Mock<OrderModule.Domain.Interface.IOrderRepository<Order>>();
            _mockIProductRepository = new Mock<OrderModule.Domain.Interface.IRepository<Product>>();
            _mockIOrderedProductRepository = new Mock<OrderModule.Domain.Interface.IRepository<OrderedProduct>>();
            _mockIProductService = new Mock<ProductModule.Application.Interface.IProductService<Product>>();

            // Build OrderService with mocked parameters
            _orderService = new OrderService(_mockIOrderRepository.Object,
                _mockIOrderRepositoryCustom.Object,
                _mockIProductRepository.Object,
                _mockIOrderedProductRepository.Object,
                _mockIProductService.Object,
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
                });

            _mockIOrderRepository.Setup(r => r.CreateAsync(It.IsAny<Order>())).ReturnsAsync(order);

            // Act
            var result = await _orderService.CreateAsync(order);

            // Assert
            Assert.NotNull(result);
            Assert.Equal(order, result);
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
                })};

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
                })};
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
                });

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
                });

            // Simulate the order already exists in the repository
            _mockIOrderRepository.Setup(r => r.GetById(orderId)).Returns(originalOrder);

            // Simulate updating the order
            var updatedOrder = new Order(originalOrder.Payday, originalOrder.PaymentScheduledDate, 200, originalOrder.EnterpriseId, originalOrder.CustomerId, originalOrder.OrderedProduct)
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

        #endregion

        #region Edge Cases
        // Order with No OrderedProducts
        [Fact]
        public async Task CreateAsync_OrderWithNoOrderedProducts_ThrowsInsertDatabaseException()
        {
            var order = new Order(DateTime.Now, DateTime.Now.AddDays(1), 100, Guid.NewGuid(), Guid.NewGuid(), new List<OrderedProduct>());
            await Assert.ThrowsAsync<InsertDatabaseException>(() => _orderService.CreateAsync(order));
        }

        // Order with Null OrderedProducts
        [Fact]
        public async Task CreateAsync_OrderWithNullOrderedProducts_ThrowsInsertDatabaseException()
        {
            var order = new Order(DateTime.Now, DateTime.Now.AddDays(1), 100, Guid.NewGuid(), Guid.NewGuid(), null);
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
                });
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
                });
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
                })
            { Id = orderId };

            _mockIOrderRepository.Setup(r => r.GetById(orderId)).Returns((Order)null);

            Assert.Throws<NotFoundDatabaseException>(() => _orderService.Update(order));
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
                })
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
                })
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
                })
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
                })
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
                })
            };
        }

        #endregion
    }
}
