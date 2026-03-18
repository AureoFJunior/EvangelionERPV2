using System.Text.Json;
using System.Text.Json.Serialization;
using EvangelionERPV2.Shared.Entities;

namespace EvangelionERPV2.Test.Bills
{
    public class LegacyOrdersTest
    {
        [Fact]
        public void DeserializeOrderPayload_MapsOrderedProducts()
        {
            var options = new JsonSerializerOptions
            {
                ReferenceHandler = ReferenceHandler.Preserve,
                PropertyNameCaseInsensitive = true,
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
                WriteIndented = true
            };

            var payload = "{\r\n\t  \"id\": \"ced34b08-e6d3-4ddc-8d83-7c03ec293335\",\r\n  \"createdAt\": \"2024-04-25T00:06:47.618Z\",\r\n  \"updatedAt\": \"2024-04-25T00:06:47.618Z\",\r\n  \"isActive\": true,\r\n  \"payday\": \"2024-04-28T00:06:47.618Z\",\r\n  \"paymentScheduledDate\": \"2024-04-27T00:06:47.618Z\",\r\n  \"totalValue\": 0.00,\r\n  \"enterpriseId\": \"3fa85f64-5717-4562-b3fc-2c963f66afa6\",\r\n  \"customerId\": \"8a090fee-5b1c-4935-90e2-08dcae91adaf\",\r\n  \"orderedProduct\": [\r\n    {\r\n      \"createdAt\": \"2024-04-25T00:06:47.619Z\",\r\n      \"updatedAt\": \"2024-04-25T00:06:47.619Z\",\r\n      \"isActive\": true,\r\n      \"quantity\": 2,\r\n      \"value\": 30.00,\r\n      \"productId\": \"18e9cdf4-ade2-4629-6f7c-08dc9bbf46c2\",\r\n\t\t\t  \"OrderId\": \"ced34b08-e6d3-4ddc-8d83-7c03ec293335\"\r\n    }\r\n  ]\r\n}";

            var order = JsonSerializer.Deserialize<Order>(payload, options);

            Assert.NotNull(order);
            Assert.NotNull(order.OrderedProduct);
            Assert.Single(order.OrderedProduct!);
            Assert.Equal(order.Id, order.OrderedProduct!.First().OrderId);
        }

        [Fact]
        public void BuildOrderEntity_WithNegativeValues_KeepsProvidedDataForServiceValidation()
        {
            var order = new Order(
                null,
                DateTime.Now.AddMonths(2),
                10,
                Guid.NewGuid(),
                Guid.NewGuid(),
                new List<OrderedProduct>
                {
                    new OrderedProduct
                    {
                        Quantity = -1,
                        Value = -10,
                        ProductId = Guid.NewGuid(),
                        IsActive = true,
                        CreatedAt = DateTime.UtcNow,
                        UpdatedAt = DateTime.UtcNow
                    }
                },
                Guid.NewGuid());

            Assert.NotNull(order);
            Assert.Single(order.OrderedProduct!);
            Assert.True(order.OrderedProduct!.First().Quantity < 0);
            Assert.True(order.OrderedProduct!.First().Value < 0);
        }
    }
}
