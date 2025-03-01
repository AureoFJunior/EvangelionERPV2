using EvangelionERPV2.OrderModule.Application.Services;
using EvangelionERPV2.Shared.Entities;
using EvangelionERPV2.Shared.Exceptions;
using Moq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace EvangelionERPV2.OrderModule.Test.Bills
{
    public class OrdersTest
    {
        private readonly Mock<OrderModule.Domain.Interface.IRepository<Order>> _mockIOrderRepository;
        private readonly Mock<OrderModule.Domain.Interface.IRepository<Product>> _mockIProductRepository;
        private readonly Mock<OrderModule.Domain.Interface.IRepository<OrderedProduct>> _mockIOrderedProductRepository;
        private readonly Mock<ProductModule.Application.Interface.IProductService<Product>> _mockIProductService;
        private readonly OrderService _orderService;

        public OrdersTest()
        {
            _mockIOrderRepository = new Mock<OrderModule.Domain.Interface.IRepository<Order>>();
            _mockIProductRepository = new Mock<OrderModule.Domain.Interface.IRepository<Product>>();
            _mockIOrderedProductRepository = new Mock<OrderModule.Domain.Interface.IRepository<OrderedProduct>>();
            _mockIProductService = new Mock<ProductModule.Application.Interface.IProductService<Product>>();

            // Build OrderService with mocked parameters
            _orderService = new OrderService(_mockIOrderRepository.Object,
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

        [Fact]
        public void TestIt()
        {
            try
            {
              var list = new List<OrderedProduct> { new OrderedProduct
                    {
                        CreatedAt = DateTime.Now,
                        UpdatedAt = DateTime.Now,
                        IsActive = true,
                        Quantity = 2,
                        Value = 0,
                        ProductId = Guid.NewGuid()
                    }, new OrderedProduct
                    {
                        CreatedAt = DateTime.Now,
                        UpdatedAt = DateTime.Now,
                        IsActive = true,
                        Quantity = 10,
                        Value = 0,
                        ProductId = Guid.NewGuid()
                    } };


                var test = $"teste {list.Count(x => x.Quantity > 1)}";
                Console.WriteLine($"teste {list.Select(x => x).ToList()}");
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.ToString());
            }
        }

        [Fact]
        public void TestIt2()
        {
            try
            {

                var options = new JsonSerializerOptions
                {
                    ReferenceHandler = ReferenceHandler.Preserve,
                    PropertyNameCaseInsensitive = true,
                    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
                    WriteIndented = true
                }; 

                var obj = JsonSerializer.Deserialize<Order>(
                    "{\r\n\t  \"id\": \"ced34b08-e6d3-4ddc-8d83-7c03ec293335\",\r\n  \"createdAt\": \"2024-04-25T00:06:47.618Z\",\r\n  \"updatedAt\": \"2024-04-25T00:06:47.618Z\",\r\n  \"isActive\": true,\r\n  \"payday\": \"2024-04-28T00:06:47.618Z\",\r\n  \"paymentScheduledDate\": \"2024-04-27T00:06:47.618Z\",\r\n  \"totalValue\": 0.00,\r\n  \"enterpriseId\": \"3fa85f64-5717-4562-b3fc-2c963f66afa6\",\r\n  \"customerId\": \"8a090fee-5b1c-4935-90e2-08dcae91adaf\",\r\n  \"orderedProduct\": [\r\n    {\r\n      \"createdAt\": \"2024-04-25T00:06:47.619Z\",\r\n      \"updatedAt\": \"2024-04-25T00:06:47.619Z\",\r\n      \"isActive\": true,\r\n      \"quantity\": 2,\r\n      \"value\": 30.00,\r\n      \"productId\": \"18e9cdf4-ade2-4629-6f7c-08dc9bbf46c2\",\r\n\t\t\t  \"OrderId\": \"ced34b08-e6d3-4ddc-8d83-7c03ec293335\"\r\n    }\r\n  ]\r\n}",
                    options);
                obj.TotalValue = 10;
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.ToString());
            }
        }
    }
}
