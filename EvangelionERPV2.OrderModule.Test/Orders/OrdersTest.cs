using EvangelionERPV2.Shared.DTOs;
using EvangelionERPV2.Shared.Configs;
using System.Linq.Expressions;
﻿using EvangelionERPV2.OrderModule.Application.Services;
using EvangelionERPV2.ProductModule.Application.DI;
using EvangelionERPV2.Shared.Entities;
using EvangelionERPV2.Shared.Utils;
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
        private readonly Mock<EvangelionERPV2.Shared.Repositories.IRepository<Customer>> _mockICustomerRepository;
        private readonly Mock<EvangelionERPV2.Shared.Repositories.IRepository<Bill>> _mockIBillRepository;
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
            _mockICustomerRepository = new Mock<EvangelionERPV2.Shared.Repositories.IRepository<Customer>>();
            SetupCustomerLookup(null);
            _mockIBillRepository = new Mock<EvangelionERPV2.Shared.Repositories.IRepository<Bill>>();
            _mockIProductService = new Mock<ProductModule.Application.Interface.IProductService<Product>>();
            SetupTransactionalExecution(_mockIOrderRepository);
            _mockIEnterpriseRepository
                .Setup(r => r.GetById(It.IsAny<Guid>()))
                .Returns((Guid enterpriseId) => new Enterprise { Id = enterpriseId, IsActive = true, Name = "Enterprise", CurrentBalance = 0 });
            _mockIEnterpriseRepository
                .Setup(r => r.GetByIdAsync(It.IsAny<Guid>()))
                .ReturnsAsync((Guid enterpriseId) => new Enterprise { Id = enterpriseId, IsActive = true, Name = "Enterprise", CurrentBalance = 0 });

            _orderReportGeneratorService = new OrderReportGeneratorService(_mockIProductRepository.Object);

            _orderService = new OrderService(_mockIOrderRepository.Object,
                _mockIOrderRepositoryCustom.Object,
                _mockIProductRepository.Object,
                _mockIOrderedProductRepository.Object,
                _mockIEnterpriseRepository.Object,
                _mockICustomerRepository.Object,
                _mockIBillRepository.Object,
                _mockIProductService.Object,
                null!,
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
                var result = await _orderService.CreateAsync(order);

                Assert.Null(result);
            }
            catch (InsertDatabaseException)
            {
                Assert.True(true);
            }
        }

        [Fact]
        public async Task CreateAsync_ValidOrder_ReturnsOrder()
        {
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

            SetupProductLookupForOrder(_mockIProductRepository, order);
            _mockIOrderRepository.Setup(r => r.CreateAsync(It.IsAny<Order>())).ReturnsAsync(order);

            var result = await _orderService.CreateAsync(order);

            Assert.NotNull(result);
            Assert.Equal(order, result);
        }

        [Fact]
        public async Task CreateAsync_IntoAClosedMonth_IsRefused()
        {
            var enterpriseId = Guid.NewGuid();
            LockPeriodThrough(new DateTime(2026, 3, 31, 0, 0, 0, DateTimeKind.Utc));

            var order = new Order(null, new DateTime(2026, 3, 10), 100, enterpriseId, Guid.NewGuid(),
                new List<OrderedProduct>
                {
                    new OrderedProduct
                    {
                        CreatedAt = DateTime.UtcNow,
                        IsActive = true,
                        Quantity = 2,
                        Value = 50,
                        ProductId = Guid.NewGuid()
                    }
                }, Guid.NewGuid());

            SetupProductLookupForOrder(_mockIProductRepository, order);
            _mockIOrderRepository.Setup(r => r.CreateAsync(It.IsAny<Order>())).ReturnsAsync(order);

            await Assert.ThrowsAsync<PeriodClosedException>(() => _orderService.CreateAsync(order));
            _mockIOrderRepository.Verify(r => r.CreateAsync(It.IsAny<Order>()), Times.Never);
        }

        [Fact]
        public async Task CreateAsync_AfterTheLock_StillWorks()
        {
            var enterpriseId = Guid.NewGuid();
            LockPeriodThrough(new DateTime(2026, 3, 31, 0, 0, 0, DateTimeKind.Utc));

            var order = new Order(null, new DateTime(2026, 4, 1), 100, enterpriseId, Guid.NewGuid(),
                new List<OrderedProduct>
                {
                    new OrderedProduct
                    {
                        CreatedAt = DateTime.UtcNow,
                        IsActive = true,
                        Quantity = 2,
                        Value = 50,
                        ProductId = Guid.NewGuid()
                    }
                }, Guid.NewGuid());

            SetupProductLookupForOrder(_mockIProductRepository, order);
            _mockIOrderRepository.Setup(r => r.CreateAsync(It.IsAny<Order>())).ReturnsAsync(order);

            var result = await _orderService.CreateAsync(order);

            Assert.NotNull(result);
            _mockIOrderRepository.Verify(r => r.CreateAsync(It.IsAny<Order>()), Times.Once);
        }

        #region Credit limit

        [Fact]
        public async Task CreateAsync_UnderTheCustomersLimit_IsTaken()
        {
            var (order, customer) = BuildOrderForCustomer(total: 400, creditLimit: 1000);
            SetupCustomer(customer);
            SetupOpenExposure(customer, []);

            var result = await _orderService.CreateAsync(order);

            Assert.NotNull(result);
            _mockIOrderRepository.Verify(r => r.CreateAsync(It.IsAny<Order>()), Times.Once);
        }

        [Fact]
        public async Task CreateAsync_OverTheLimitOnceTheOpenCarteiraCounts_IsRefused()
        {
            var (order, customer) = BuildOrderForCustomer(total: 400, creditLimit: 1000);
            SetupCustomer(customer);

            SetupOpenExposure(customer, [BuildOpenOrder(customer, 700)]);

            await Assert.ThrowsAsync<CreditLimitExceededException>(() => _orderService.CreateAsync(order));
            _mockIOrderRepository.Verify(r => r.CreateAsync(It.IsAny<Order>()), Times.Never);
        }

        [Fact]
        public async Task CreateAsync_WithNoLimitSet_IgnoresTheCarteiraEntirely()
        {
            var (order, customer) = BuildOrderForCustomer(total: 9000, creditLimit: null);
            SetupCustomer(customer);
            SetupOpenExposure(customer, [BuildOpenOrder(customer, 50000)]);

            Assert.NotNull(await _orderService.CreateAsync(order));
        }

        [Fact]
        public async Task CreateAsync_WithTheOverride_TakesTheOrderAnyway()
        {
            var (order, customer) = BuildOrderForCustomer(total: 400, creditLimit: 1000);
            SetupCustomer(customer);
            SetupOpenExposure(customer, [BuildOpenOrder(customer, 700)]);

            Assert.NotNull(await _orderService.CreateAsync(order, allowCreditOverride: true));
        }

        [Fact]
        public async Task CreateAsync_LocksTheCustomersRowBeforeReadingTheCarteira()
        {
            var (order, customer) = BuildOrderForCustomer(total: 400, creditLimit: 1000);
            SetupCustomer(customer);
            SetupOpenExposure(customer, []);

            var steps = new List<string>();

            _mockIOrderRepository
                .Setup(r => r.ExecuteInTransactionAsync(It.IsAny<Func<Task>>(), It.IsAny<CancellationToken>()))
                .Returns((Func<Task> operation, CancellationToken _) =>
                {
                    steps.Add("transaction");
                    return operation();
                });

            _mockICustomerRepository
                .Setup(r => r.LockForUpdateAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
                .Callback(() => steps.Add("lock"))
                .Returns(Task.CompletedTask);

            _mockIOrderRepository
                .Setup(r => r.GetPagedAsync(
                    It.IsAny<Expression<Func<Order, bool>>?>(),
                    It.IsAny<Func<IQueryable<Order>, IOrderedQueryable<Order>>?>(),
                    It.IsAny<int?>(),
                    It.IsAny<int?>()))
                .Callback(() => steps.Add("carteira"))
                .ReturnsAsync(((IReadOnlyList<Order>)new List<Order>(), 0));

            await _orderService.CreateAsync(order);

            Assert.Equal(new[] { "transaction", "lock", "carteira" }, steps);
            _mockICustomerRepository.Verify(
                r => r.LockForUpdateAsync(customer.Id, It.IsAny<CancellationToken>()), Times.Once);
            _mockICustomerRepository.Verify(
                r => r.LockForUpdateAsync(It.Is<Guid>(id => id != customer.Id), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public async Task CreateAsync_WithNoLimitSet_LocksNothing()
        {
            var (order, customer) = BuildOrderForCustomer(total: 9000, creditLimit: null);
            SetupCustomer(customer);
            SetupOpenExposure(customer, []);

            await _orderService.CreateAsync(order);

            _mockICustomerRepository.Verify(
                r => r.LockForUpdateAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public async Task CreateAsync_DoesNotCountARefundedOrderAgainstTheLimit()
        {
            var (order, customer) = BuildOrderForCustomer(total: 400, creditLimit: 1000);
            SetupCustomer(customer);

            var refunded = BuildOpenOrder(customer, 700);
            refunded.RefundedAt = DateTime.UtcNow.AddDays(-1);
            refunded.Status = (int)EnumOrderStatus.Refund;
            SetupOpenExposure(customer, [refunded]);

            Assert.NotNull(await _orderService.CreateAsync(order));
        }

        [Fact]
        public async Task CreateAsync_DoesNotCountAnOrderWhoseBoletoIsSettled()
        {
            var (order, customer) = BuildOrderForCustomer(total: 400, creditLimit: 1000);
            SetupCustomer(customer);

            var settled = BuildOpenOrder(customer, 700);
            var bill = new Bill
            {
                Id = Guid.NewGuid(),
                OrderId = settled.Id,
                Order = settled,
                IsActive = true,
                Amount = 700,
                DueDate = DateTime.UtcNow.Date.AddDays(-5),
                Status = BillStatus.Paid,
                PaidAt = DateTime.UtcNow.Date.AddDays(-4),
                PaidAmount = 700
            };

            SetupOpenExposure(customer, [settled], [bill]);

            Assert.NotNull(await _orderService.CreateAsync(order));
        }

        private (Order Order, Customer Customer) BuildOrderForCustomer(decimal total, decimal? creditLimit)
        {
            var enterpriseId = Guid.NewGuid();
            var customer = new Customer
            {
                Id = Guid.NewGuid(),
                EnterpriseId = enterpriseId,
                Name = "Asuka",
                IsActive = true,
                CreditLimit = creditLimit
            };

            var order = new Order(null, DateTime.UtcNow.Date.AddDays(30), total, enterpriseId, customer.Id,
                new List<OrderedProduct>
                {
                    new OrderedProduct
                    {
                        CreatedAt = DateTime.UtcNow,
                        IsActive = true,
                        Quantity = 1,
                        Value = total,
                        ProductId = Guid.NewGuid()
                    }
                }, Guid.NewGuid());

            SetupProductLookupForOrder(_mockIProductRepository, order);
            _mockIOrderRepository.Setup(r => r.CreateAsync(It.IsAny<Order>())).ReturnsAsync(order);

            return (order, customer);
        }

        private Order BuildOpenOrder(Customer customer, decimal total) => new()
        {
            Id = Guid.NewGuid(),
            EnterpriseId = customer.EnterpriseId,
            CustomerId = customer.Id,
            IsActive = true,
            TotalValue = total,
            Status = (int)EnumOrderStatus.Pending,
            PaymentScheduledDate = DateTime.UtcNow.Date.AddDays(10)
        };

        [Fact]
        public void Delete_OfAFinishedOrder_ReportsTheRuleRatherThanCrashing()
        {
            var order = new Order
            {
                Id = Guid.NewGuid(),
                EnterpriseId = Guid.NewGuid(),
                IsActive = true,
                Status = (int)EnumOrderStatus.Finished,
                PaymentScheduledDate = DateTime.UtcNow.Date
            };

            _mockIOrderRepository.Setup(r => r.GetById(order.Id)).Returns(order);

            var exception = Assert.Throws<InsertDatabaseException>(
                () => _orderService.Delete(order.Id, order.EnterpriseId!.Value));

            Assert.Equal("Orders with status Finished or Refund cannot be edited.", exception.Message);
            Assert.Null(exception.InnerException);
        }

        /// <summary>A customer repository that answers "no such customer" the way the real one does.</summary>
        private static EvangelionERPV2.Shared.Repositories.IRepository<Customer> EmptyCustomerRepository()
        {
            var repository = new Mock<EvangelionERPV2.Shared.Repositories.IRepository<Customer>>();
            repository
                .Setup(r => r.GetPagedAsync(
                    It.IsAny<System.Linq.Expressions.Expression<Func<Customer, bool>>?>(),
                    It.IsAny<Func<IQueryable<Customer>, IOrderedQueryable<Customer>>?>(),
                    It.IsAny<int?>(),
                    It.IsAny<int?>()))
                .ReturnsAsync(((IReadOnlyList<Customer>)new List<Customer>(), 0));

            return repository.Object;
        }

        private void SetupCustomer(Customer customer) => SetupCustomerLookup(customer);

        private void SetupCustomerLookup(Customer? customer)
        {
            var records = customer == null ? new List<Customer>() : [customer];

            _mockICustomerRepository
                .Setup(r => r.GetPagedAsync(
                    It.IsAny<System.Linq.Expressions.Expression<Func<Customer, bool>>?>(),
                    It.IsAny<Func<IQueryable<Customer>, IOrderedQueryable<Customer>>?>(),
                    It.IsAny<int?>(),
                    It.IsAny<int?>()))
                .ReturnsAsync((
                    System.Linq.Expressions.Expression<Func<Customer, bool>>? predicate,
                    Func<IQueryable<Customer>, IOrderedQueryable<Customer>>? _,
                    int? __,
                    int? ___) =>
                {
                    var items = predicate == null ? records : records.Where(predicate.Compile()).ToList();
                    return ((IReadOnlyList<Customer>)items, items.Count);
                });
        }

        private void SetupOpenExposure(Customer customer, List<Order> openOrders, List<Bill>? bills = null)
        {
            _mockIOrderRepository
                .Setup(r => r.GetPagedAsync(
                    It.IsAny<System.Linq.Expressions.Expression<Func<Order, bool>>?>(),
                    It.IsAny<Func<IQueryable<Order>, IOrderedQueryable<Order>>?>(),
                    It.IsAny<int?>(),
                    It.IsAny<int?>()))
                .ReturnsAsync((
                    System.Linq.Expressions.Expression<Func<Order, bool>>? predicate,
                    Func<IQueryable<Order>, IOrderedQueryable<Order>>? _,
                    int? __,
                    int? ___) =>
                {
                    var items = predicate == null ? openOrders : openOrders.Where(predicate.Compile()).ToList();
                    return ((IReadOnlyList<Order>)items, items.Count);
                });

            _mockIBillRepository
                .Setup(r => r.GetAllAsync(It.IsAny<System.Linq.Expressions.Expression<Func<Bill, bool>>?>()))
                .ReturnsAsync((System.Linq.Expressions.Expression<Func<Bill, bool>>? _) => bills ?? []);
        }

        #endregion

        private void LockPeriodThrough(DateTime closedThroughUtc)
        {
            _mockIEnterpriseRepository
                .Setup(r => r.GetById(It.IsAny<Guid>()))
                .Returns((Guid enterpriseId) => new Enterprise
                {
                    Id = enterpriseId,
                    IsActive = true,
                    Name = "Enterprise",
                    ClosedThroughUtc = closedThroughUtc
                });

            _mockIEnterpriseRepository
                .Setup(r => r.GetByIdAsync(It.IsAny<Guid>()))
                .ReturnsAsync((Guid enterpriseId) => new Enterprise
                {
                    Id = enterpriseId,
                    IsActive = true,
                    Name = "Enterprise",
                    ClosedThroughUtc = closedThroughUtc
                });
        }

        [Fact]
        public async Task CreateAsync_IgnoresClientProvidedIds_GeneratesServerIds()
        {
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
            SetupProductLookupForOrder(_mockIProductRepository, order);

            var result = await _orderService.CreateAsync(order);

            Assert.NotNull(createdOrder);
            Assert.NotEqual(incomingOrderId, result.Id);
            Assert.Equal(result.Id, createdOrder?.Id);

            var line = createdLines?.SingleOrDefault();
            Assert.NotNull(line);
            Assert.NotEqual(incomingLineId, line?.Id ?? Guid.Empty);
            Assert.Equal(result.Id, line?.OrderId);
        }

        [Fact]
        public async Task CreateAsync_SamePayloadTwice_CreatesTwoIndependentOrders()
        {
            var enterpriseId = Guid.NewGuid();
            var customerId = Guid.NewGuid();
            var userId = Guid.NewGuid();
            var productId = Guid.NewGuid();
            var now = DateTime.UtcNow;

            Order BuildOrder()
            {
                return new Order(now.AddDays(1), now.AddDays(2), 100, enterpriseId, customerId,
                    new List<OrderedProduct>
                    {
                        new OrderedProduct
                        {
                            CreatedAt = now,
                            UpdatedAt = now,
                            IsActive = true,
                            Quantity = 2,
                            Value = 50,
                            ProductId = productId
                        }
                    }, userId);
            }

            var persistedOrders = new List<Order>();

            _mockIOrderRepository
                .Setup(r => r.GetByIdAsync(It.IsAny<Guid>()))
                .ReturnsAsync((Guid id) => persistedOrders.FirstOrDefault(order => order.Id == id)!);
            _mockIOrderRepository
                .Setup(r => r.CreateAsync(It.IsAny<Order>()))
                .Callback<Order>(entity => persistedOrders.Add(entity))
                .ReturnsAsync((Order entity) => entity);
            _mockIOrderRepository
                .Setup(r => r.CommitAsync(It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);
            _mockIOrderedProductRepository
                .Setup(r => r.CreateRangeAsync(It.IsAny<IEnumerable<OrderedProduct>>()))
                .ReturnsAsync((IEnumerable<OrderedProduct> items) => items);
            _mockIOrderedProductRepository
                .Setup(r => r.CommitAsync(It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);
            _mockIProductService
                .Setup(p => p.UpdateForOrder(It.IsAny<Order>()))
                .Returns(Task.CompletedTask);

            var firstPayload = BuildOrder();
            SetupProductLookupForOrder(_mockIProductRepository, firstPayload);

            var firstResult = await _orderService.CreateAsync(firstPayload);
            var secondResult = await _orderService.CreateAsync(BuildOrder());

            Assert.NotEqual(firstResult.Id, secondResult.Id);
            Assert.Equal(2, persistedOrders.Count);
            _mockIOrderRepository.Verify(r => r.CreateAsync(It.IsAny<Order>()), Times.Exactly(2));
            _mockIOrderedProductRepository.Verify(r => r.CreateRangeAsync(It.IsAny<IEnumerable<OrderedProduct>>()), Times.Exactly(2));
            _mockIProductService.Verify(p => p.UpdateForOrder(It.IsAny<Order>()), Times.Exactly(2));
        }

        [Theory]
        [MemberData(nameof(GetInvalidOrders))]
        public async Task CreateAsync_InvalidOrder_ThrowsInsertDatabaseException(Order invalidOrder)
        {
            await Assert.ThrowsAsync<InsertDatabaseException>(() => _orderService.CreateAsync(invalidOrder));
        }

        public static IEnumerable<object[]> GetInvalidOrders()
        {
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

            var refOrder = order;
            _orderService.VerifyValidValues(ref refOrder);
        }

        [Fact]
        public void Update_ValidOrder_ReturnsUpdatedOrder()
        {
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

            _mockIOrderRepository.Setup(r => r.GetById(orderId)).Returns(originalOrder);

            var updatedOrder = new Order(originalOrder.Payday, originalOrder.PaymentScheduledDate, 200, originalOrder.EnterpriseId, originalOrder.CustomerId, originalOrder.OrderedProduct!, Guid.NewGuid())
            {
                Id = orderId
            };

            _mockIOrderRepository.Setup(r => r.Update(It.IsAny<Order>())).Returns(updatedOrder);

            var result = _orderService.Update(updatedOrder);

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

        [Theory]
        [MemberData(nameof(GetPaydayStatusTransitions))]
        public void Update_WhenStatusChangesToPaydayStatus_ShouldSetPaydayToTransitionDate(
            int currentStatus,
            int requestedStatus)
        {
            var orderId = Guid.NewGuid();
            var enterpriseId = Guid.NewGuid();
            var existentOrder = new Order
            {
                Id = orderId,
                EnterpriseId = enterpriseId,
                IsActive = true,
                Status = currentStatus,
                TotalValue = 100,
                Payday = null,
                PaymentScheduledDate = DateTime.UtcNow.AddDays(2)
            };

            var payload = new Order
            {
                Id = orderId,
                EnterpriseId = enterpriseId,
                IsActive = true,
                Status = requestedStatus,
                TotalValue = 100,
                Payday = null,
                PaymentScheduledDate = existentOrder.PaymentScheduledDate
            };

            _mockIOrderRepository.Setup(r => r.GetById(orderId)).Returns(existentOrder);
            _mockIOrderRepository.Setup(r => r.Update(It.IsAny<Order>())).Returns((Order entity) => entity);

            var beforeUpdate = DateTime.UtcNow;
            var result = _orderService.Update(payload, enterpriseId);
            var afterUpdate = DateTime.UtcNow;

            Assert.Equal(requestedStatus, result.Status);
            Assert.NotNull(result.Payday);
            Assert.InRange(result.Payday.Value, beforeUpdate, afterUpdate);
            _mockIOrderRepository.Verify(
                r => r.Update(It.Is<Order>(o => o.Id == orderId && o.Payday.HasValue)),
                Times.Once);
        }

        /// <summary>
        /// The store types the tracking code when it marks an order Shipped (storefront spec E7). The
        /// shopper's order page and e-mail show it, so it is stored trimmed.
        /// </summary>
        [Fact]
        public void Update_ToShippedWithTracking_StoresTheTrimmedCodeAndLink()
        {
            var (existentOrder, payload) = ShippingOrder(existingCode: null);
            payload.TrackingCode = "  BR123456789BR ";
            payload.TrackingUrl = " https://rastreamento.correios.com.br/app/index.php?objeto=BR123456789BR ";

            var result = _orderService.Update(payload, existentOrder.EnterpriseId!.Value);

            Assert.Equal("BR123456789BR", result.TrackingCode);
            Assert.Equal("https://rastreamento.correios.com.br/app/index.php?objeto=BR123456789BR", result.TrackingUrl);
        }

        [Fact]
        public void Update_WithoutTracking_KeepsTheTrackingAlreadyStored()
        {
            var (existentOrder, payload) = ShippingOrder(existingCode: "BR123456789BR");
            payload.Status = (int)EnumOrderStatus.Delivered;

            var result = _orderService.Update(payload, existentOrder.EnterpriseId!.Value);

            Assert.Equal("BR123456789BR", result.TrackingCode);
            Assert.Equal("https://example.com/track/BR123456789BR", result.TrackingUrl);
        }

        [Fact]
        public void Update_WithBlankTracking_ClearsAMistypedCode()
        {
            var (existentOrder, payload) = ShippingOrder(existingCode: "BR-TYPO");
            payload.TrackingCode = " ";
            payload.TrackingUrl = "";

            var result = _orderService.Update(payload, existentOrder.EnterpriseId!.Value);

            Assert.Null(result.TrackingCode);
            Assert.Null(result.TrackingUrl);
        }

        [Fact]
        public void Update_ToShipped_StampsShippedAtOnce_AndDeliveredStampsDeliveredAt()
        {
            var (existentOrder, payload) = ShippingOrder(existingCode: null);
            var earlier = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc);

            var shipped = _orderService.Update(payload, existentOrder.EnterpriseId!.Value);
            Assert.NotNull(shipped.ShippedAt);
            Assert.Null(shipped.DeliveredAt);

            existentOrder.ShippedAt = earlier;
            payload.Status = (int)EnumOrderStatus.Delivered;
            var delivered = _orderService.Update(payload, existentOrder.EnterpriseId!.Value);

            Assert.Equal(earlier, delivered.ShippedAt);
            Assert.NotNull(delivered.DeliveredAt);
        }

        [Fact]
        public void VerifyValidValues_KeepsSettledOrderDatesToday_AndGivesAnUnpaidOneThirtyDays()
        {
            var today = DateTime.UtcNow;
            Order Build(EnumOrderStatus status) => new()
            {
                Status = (int)status,
                PaymentScheduledDate = today,
                OrderedProduct = [new OrderedProduct { Quantity = 1, Value = 10m, ProductId = Guid.NewGuid() }]
            };

            var paid = Build(EnumOrderStatus.Paid);
            var finished = Build(EnumOrderStatus.Finished);
            var pending = Build(EnumOrderStatus.Pending);
            _orderService.VerifyValidValues(ref paid);
            _orderService.VerifyValidValues(ref finished);
            _orderService.VerifyValidValues(ref pending);

            Assert.Equal(today, paid.PaymentScheduledDate);
            Assert.Equal(today, finished.PaymentScheduledDate);
            Assert.Equal(today.AddDays(30), pending.PaymentScheduledDate);
        }

        [Fact]
        public async Task GetByFilter_WithAChannel_ReturnsOnlyThatChannelsOrders()
        {
            var enterpriseId = Guid.NewGuid();
            System.Linq.Expressions.Expression<Func<Order, bool>>? predicate = null;
            _mockIOrderRepository
                .Setup(r => r.GetAllAsyncByFilter(It.IsAny<bool>(), It.IsAny<int?>(), It.IsAny<int?>(),
                    It.IsAny<System.Linq.Expressions.Expression<Func<Order, bool>>>(), It.IsAny<System.Linq.Expressions.Expression<Func<Order, object>>>()))
                .Callback((bool _, int? _, int? _, System.Linq.Expressions.Expression<Func<Order, bool>>? p, System.Linq.Expressions.Expression<Func<Order, object>>? _) => predicate = p)
                .ReturnsAsync([]);

            await _orderService.GetByFilterAsync(enterpriseId, new OrderFilterRequestDTO { Channel = (int)EnumOrderChannel.Storefront }, true, 1, 20);

            var matches = predicate!.Compile();
            Assert.True(matches(new Order { EnterpriseId = enterpriseId, IsActive = true, Channel = (int)EnumOrderChannel.Storefront }));
            Assert.False(matches(new Order { EnterpriseId = enterpriseId, IsActive = true, Channel = (int)EnumOrderChannel.Erp }));
        }

        [Fact]
        public async Task GetByFilter_AppliesSearchAndRequestedSortBeforePaging()
        {
            var enterpriseId = Guid.NewGuid();
            System.Linq.Expressions.Expression<Func<Order, bool>>? predicate = null;
            System.Linq.Expressions.Expression<Func<Order, object>>? sort = null;
            _mockIOrderRepository
                .Setup(r => r.GetAllAsyncByFilter(It.IsAny<bool>(), It.IsAny<int?>(), It.IsAny<int?>(),
                    It.IsAny<System.Linq.Expressions.Expression<Func<Order, bool>>>(), It.IsAny<System.Linq.Expressions.Expression<Func<Order, object>>>()))
                .Callback((bool _, int? _, int? _, System.Linq.Expressions.Expression<Func<Order, bool>>? p, System.Linq.Expressions.Expression<Func<Order, object>>? s) =>
                {
                    predicate = p;
                    sort = s;
                })
                .ReturnsAsync([]);

            await _orderService.GetByFilterAsync(enterpriseId, new OrderFilterRequestDTO { Search = "Ada", SortBy = "total" }, false, 1, 20);

            Assert.Contains("Like", predicate!.ToString());
            Assert.Equal(25m, sort!.Compile()(new Order { TotalValue = 25m }));
        }

        [Fact]
        public async Task GetByFilter_SupportsEverySortKeyAndFallsBackToCreatedAt()
        {
            var enterpriseId = Guid.NewGuid();
            System.Linq.Expressions.Expression<Func<Order, object>>? sort = null;
            _mockIOrderRepository
                .Setup(r => r.GetAllAsyncByFilter(It.IsAny<bool>(), It.IsAny<int?>(), It.IsAny<int?>(),
                    It.IsAny<System.Linq.Expressions.Expression<Func<Order, bool>>>(), It.IsAny<System.Linq.Expressions.Expression<Func<Order, object>>>() ))
                .Callback((bool _, int? _, int? _, System.Linq.Expressions.Expression<Func<Order, bool>>? _, System.Linq.Expressions.Expression<Func<Order, object>>? requestedSort) => sort = requestedSort)
                .ReturnsAsync([]);
            var createdAt = new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc);
            var order = new Order
            {
                Id = Guid.NewGuid(),
                Customer = new Customer { Name = "Ada" },
                OrderedProduct = [new OrderedProduct()],
                TotalValue = 25m,
                Status = 3,
                CreatedAt = createdAt
            };

            foreach (var (sortBy, expected) in new (string, object)[]
            {
                ("id", order.Id), ("customer", "Ada"), ("items", 1), ("total", 25m), ("status", 3), ("other", createdAt)
            })
            {
                await _orderService.GetByFilterAsync(enterpriseId, new OrderFilterRequestDTO { SortBy = sortBy }, false, 1, 20);
                Assert.Equal(expected, sort!.Compile()(order));
            }
        }

        [Fact]
        public void ALimenOrder_MapsItsDeliveryAddressShippingAndNumber_AndOtherOrdersCarryNone()
        {
            var mapper = MapperConfig.RegisterMaps().CreateMapper();
            var id = Guid.Parse("da7c68cd-0000-4000-8000-000000000001");
            var limen = new Order
            {
                Id = id,
                Channel = (int)EnumOrderChannel.Storefront,
                ShippingValue = 15m,
                DeliveryAddress = """{"Cep":"01310100","Street":"Avenida Paulista","Number":"1000","Complement":null,"District":"Bela Vista","City":"São Paulo","Uf":"SP"}""",
                ShippedAt = new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc)
            };

            var dto = mapper.Map<OrderDTO>(limen);

            Assert.Equal(((int)EnumOrderChannel.Storefront, 15m, "LM-DA7C68CD"), (dto.Channel, dto.ShippingValue, dto.StorefrontOrderNumber));
            Assert.Equal(("Avenida Paulista", "1000", "São Paulo", "SP", "01310100"),
                (dto.DeliveryAddress!.Street, dto.DeliveryAddress.Number, dto.DeliveryAddress.City, dto.DeliveryAddress.Uf, dto.DeliveryAddress.Cep));
            Assert.Equal(limen.ShippedAt, dto.ShippedAt);

            var manual = mapper.Map<OrderDTO>(new Order { Id = Guid.NewGuid() });
            Assert.Null(manual.DeliveryAddress);
            Assert.Null(manual.StorefrontOrderNumber);
        }

        [Fact]
        public void VerifyValidValues_AddsShippingToTheTotal()
        {
            var order = new Order
            {
                ShippingValue = 20m,
                OrderedProduct = [new OrderedProduct { Quantity = 2, Value = 35m, ProductId = Guid.NewGuid() }]
            };

            _orderService.VerifyValidValues(ref order);

            Assert.Equal(90m, order.TotalValue);
        }

        [Fact]
        public void ALimenOrder_StaysPaidOnItsPaymentDay_WhenTheStoreSeparatesAndShipsIt()
        {
            var paidAt = new DateTime(2026, 9, 30, 12, 0, 0, DateTimeKind.Utc);
            var (existentOrder, payload) = ShippingOrder(existingCode: null);
            existentOrder.Channel = (int)EnumOrderChannel.Storefront;
            existentOrder.Payday = paidAt;

            payload.Status = (int)EnumOrderStatus.Processing;
            var separating = _orderService.Update(payload, existentOrder.EnterpriseId!.Value);
            Assert.Equal(paidAt, separating.Payday);

            payload.Status = (int)EnumOrderStatus.Shipped;
            var shipped = _orderService.Update(payload, existentOrder.EnterpriseId!.Value);
            Assert.Equal(paidAt, shipped.Payday);
        }

        [Fact]
        public void AnErpOrderMovedBackToProcessing_IsStillUnpaid()
        {
            var (existentOrder, payload) = ShippingOrder(existingCode: null);
            existentOrder.Payday = new DateTime(2026, 9, 30, 12, 0, 0, DateTimeKind.Utc);
            payload.Status = (int)EnumOrderStatus.Processing;

            Assert.Null(_orderService.Update(payload, existentOrder.EnterpriseId!.Value).Payday);
        }

        private (Order Existent, Order Payload) ShippingOrder(string? existingCode)
        {
            var orderId = Guid.NewGuid();
            var enterpriseId = Guid.NewGuid();
            var existentOrder = new Order
            {
                Id = orderId,
                EnterpriseId = enterpriseId,
                IsActive = true,
                Status = (int)EnumOrderStatus.Paid,
                TotalValue = 100,
                PaymentScheduledDate = DateTime.UtcNow.AddDays(2),
                TrackingCode = existingCode,
                TrackingUrl = existingCode is null ? null : $"https://example.com/track/{existingCode}"
            };
            var payload = new Order
            {
                Id = orderId,
                EnterpriseId = enterpriseId,
                IsActive = true,
                Status = (int)EnumOrderStatus.Shipped,
                PaymentScheduledDate = existentOrder.PaymentScheduledDate
            };

            _mockIOrderRepository.Setup(r => r.GetById(orderId)).Returns(existentOrder);
            _mockIOrderRepository.Setup(r => r.Update(It.IsAny<Order>())).Returns((Order entity) => entity);
            return (existentOrder, payload);
        }

        [Fact]
        public void Update_WhenStatusChangesAwayFromPaydayStatus_ShouldClearPayday()
        {
            var orderId = Guid.NewGuid();
            var enterpriseId = Guid.NewGuid();
            var existentOrder = new Order
            {
                Id = orderId,
                EnterpriseId = enterpriseId,
                IsActive = true,
                Status = (int)EnumOrderStatus.Paid,
                TotalValue = 100,
                Payday = DateTime.UtcNow.AddDays(-1),
                PaymentScheduledDate = DateTime.UtcNow.AddDays(2)
            };

            var payload = new Order
            {
                Id = orderId,
                EnterpriseId = enterpriseId,
                IsActive = true,
                Status = (int)EnumOrderStatus.Processing,
                TotalValue = 100,
                Payday = null,
                PaymentScheduledDate = existentOrder.PaymentScheduledDate
            };

            _mockIOrderRepository.Setup(r => r.GetById(orderId)).Returns(existentOrder);
            _mockIOrderRepository.Setup(r => r.Update(It.IsAny<Order>())).Returns((Order entity) => entity);

            var result = _orderService.Update(payload, enterpriseId);

            Assert.Equal((int)EnumOrderStatus.Processing, result.Status);
            Assert.Null(result.Payday);
            _mockIOrderRepository.Verify(
                r => r.Update(It.Is<Order>(o => o.Id == orderId && o.Payday == null)),
                Times.Once);
        }

        /// <summary>
        /// Finished is in here on purpose. It blocks edits and deletes, and used to block this too
        /// — which would have made every till sale unrefundable the moment PDV started creating
        /// them Finished. A finished sale is precisely what a refund reverses.
        /// </summary>
        [Theory]
        [InlineData((int)EnumOrderStatus.Processing)]
        [InlineData((int)EnumOrderStatus.Finished)]
        public async Task RefundAsync_ValidOrder_ShouldRestoreStockAndSetRefundState(int status)
        {
            var enterpriseId = Guid.NewGuid();
            var orderId = Guid.NewGuid();
            var productId = Guid.NewGuid();

            var existentOrder = new Order
            {
                Id = orderId,
                EnterpriseId = enterpriseId,
                IsActive = true,
                Status = status,
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
                .Setup(r => r.GetAllAsync(It.IsAny<Expression<Func<OrderedProduct, bool>>?>()))
                .ReturnsAsync((Expression<Func<OrderedProduct, bool>>? predicate) =>
                {
                    IEnumerable<OrderedProduct> query = orderedProducts;
                    if (predicate != null)
                        query = query.Where(predicate.Compile());
                    return query;
                });

            _mockIProductRepository
                .Setup(r => r.GetAllAsync(It.IsAny<Expression<Func<Product, bool>>?>()))
                .ReturnsAsync((Expression<Func<Product, bool>>? predicate) =>
                {
                    IEnumerable<Product> query = products;
                    if (predicate != null)
                        query = query.Where(predicate.Compile());
                    return query;
                });

            var refunded = await _orderService.RefundAsync(orderId, enterpriseId, "Customer returned wrong item");

            Assert.Equal((int)EnumOrderStatus.Refund, refunded.Status);
            Assert.Equal(0, refunded.TotalValue);
            Assert.Equal("Customer returned wrong item", refunded.RefundReason);
            Assert.NotNull(refunded.RefundedAt);
            Assert.Equal(0, orderedProducts[0].Quantity);
            Assert.Equal(0, orderedProducts[0].Value);

            _mockIProductService.Verify(
                s => s.ApplyStockMovementAsync(
                    productId,
                    enterpriseId,
                    4m,
                    EnumStockMovementType.Refund,
                    orderId,
                    false),
                Times.Once);
            _mockIOrderedProductRepository.Verify(r => r.UpdateRange(It.IsAny<IEnumerable<OrderedProduct>>()), Times.Once);
            _mockIOrderRepository.Verify(r => r.Update(It.IsAny<Order>()), Times.Once);
        }

        [Fact]
        public async Task RefundAsync_WhenReasonIsEmpty_ShouldThrowInsertDatabaseException()
        {
            await Assert.ThrowsAsync<InsertDatabaseException>(
                () => _orderService.RefundAsync(Guid.NewGuid(), Guid.NewGuid(), " "));
        }

        /// <summary>Already refunded is the one status a refund still refuses.</summary>
        [Theory]
        [InlineData((int)EnumOrderStatus.Refund, false)]
        [InlineData((int)EnumOrderStatus.Finished, true)]
        public async Task RefundAsync_WhenAlreadyRefunded_ShouldThrowInsertDatabaseException(
            int status, bool refundedAt)
        {
            var enterpriseId = Guid.NewGuid();
            var order = new Order
            {
                Id = Guid.NewGuid(),
                EnterpriseId = enterpriseId,
                IsActive = true,
                Status = status,

                RefundedAt = refundedAt ? DateTime.UtcNow : null
            };

            _mockIOrderRepository.Setup(r => r.GetByIdAsync(order.Id)).ReturnsAsync(order);

            await Assert.ThrowsAsync<InsertDatabaseException>(
                () => _orderService.RefundAsync(order.Id, enterpriseId, "Cannot refund twice"));
        }

        #endregion

        #region Edge Cases
        [Fact]
        public async Task CreateAsync_OrderWithNoOrderedProducts_ThrowsInsertDatabaseException()
        {
            var order = new Order(DateTime.Now, DateTime.Now.AddDays(1), 100, Guid.NewGuid(), Guid.NewGuid(), new List<OrderedProduct>(), Guid.NewGuid());
            await Assert.ThrowsAsync<InsertDatabaseException>(() => _orderService.CreateAsync(order));
        }

        [Fact]
        public async Task CreateAsync_OrderWithNullOrderedProducts_ThrowsInsertDatabaseException()
        {
            var order = new Order(DateTime.Now, DateTime.Now.AddDays(1), 100, Guid.NewGuid(), Guid.NewGuid(), null!, Guid.NewGuid());
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

        [Fact]
        public async Task CreateAsync_OrderWithLargeQuantityOrValue_ThrowsInsertDatabaseException()
        {
            var order = new Order(DateTime.Now, DateTime.Now.AddDays(1), decimal.MaxValue, Guid.NewGuid(), Guid.NewGuid(),
                new List<OrderedProduct>
                {
            new OrderedProduct { CreatedAt = DateTime.Now, UpdatedAt = DateTime.Now, IsActive = true, Quantity = decimal.MaxValue, Value = decimal.MaxValue, ProductId = Guid.NewGuid() }
                }, Guid.NewGuid());
            await Assert.ThrowsAsync<InsertDatabaseException>(() => _orderService.CreateAsync(order));
        }

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

            _mockIOrderRepository.Setup(r => r.GetById(orderId)).Returns((Order)null!);

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

        public static IEnumerable<object[]> GetPaydayStatusTransitions()
        {
            yield return new object[] { (int)EnumOrderStatus.Processing, (int)EnumOrderStatus.Paid };
            yield return new object[] { (int)EnumOrderStatus.Processing, (int)EnumOrderStatus.Shipped };
            yield return new object[] { (int)EnumOrderStatus.Processing, (int)EnumOrderStatus.Delivered };
            yield return new object[] { (int)EnumOrderStatus.Delivered, (int)EnumOrderStatus.Finished };
        }

        public static IEnumerable<object[]> GetOrdersWithNegativeValues()
        {
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
        public void OrderHub_RequiresOrdersReadPolicy()
        {
            var policy = typeof(OrderHub)
                .GetCustomAttributes(typeof(Microsoft.AspNetCore.Authorization.AuthorizeAttribute), true)
                .Cast<Microsoft.AspNetCore.Authorization.AuthorizeAttribute>()
                .SingleOrDefault(x => x.Policy == "rbac:orders.read");

            Assert.NotNull(policy);
        }

        [Fact]
        public async Task CreateAsync_SendsSignalRNotification()
        {
            var enterpriseId = Guid.NewGuid();
            var order = new Order(DateTime.UtcNow, DateTime.UtcNow.AddDays(1), 100, enterpriseId, Guid.NewGuid(),
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

            mockOrderRepo.Setup(r => r.CreateAsync(It.IsAny<Order>())).ReturnsAsync(order);
            mockOrderRepo.Setup(r => r.CommitAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
            mockOrderedProductRepo.Setup(r => r.CreateRangeAsync(It.IsAny<IEnumerable<OrderedProduct>>()))
                .Returns(Task.FromResult<IEnumerable<OrderedProduct>>(order.OrderedProduct!));
            mockOrderedProductRepo.Setup(r => r.CommitAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
            mockProductService.Setup(p => p.UpdateForOrder(It.IsAny<Order>())).Returns(Task.CompletedTask);
            SetupProductLookupForOrder(mockProductRepo, order);

            var mockHubContext = new Mock<IHubContext<OrderHub>>();
            var mockClients = new Mock<IHubClients>();
            var mockClientProxy = new Mock<IClientProxy>();
            var mockGroupProxy = new Mock<IClientProxy>();

            mockHubContext.SetupGet(h => h.Clients).Returns(mockClients.Object);
            mockClients.Setup(c => c.All).Returns(mockClientProxy.Object);
            mockClients.Setup(c => c.Group(enterpriseId.ToString())).Returns(mockGroupProxy.Object);
            mockClientProxy.Setup(p => p.SendCoreAsync(It.IsAny<string>(), It.IsAny<object[]>(), It.IsAny<CancellationToken>()))
                           .Returns(Task.CompletedTask);
            mockGroupProxy.Setup(p => p.SendCoreAsync(It.IsAny<string>(), It.IsAny<object[]>(), It.IsAny<CancellationToken>()))
                          .Returns(Task.CompletedTask);

            var orderReportGeneratorService = new OrderReportGeneratorService(mockProductRepo.Object);

            var orderService = new OrderService(
                mockOrderRepo.Object,
                mockOrderRepoCustom.Object,
                mockProductRepo.Object,
                mockOrderedProductRepo.Object,
                mockEnterpriseRepo.Object,
                EmptyCustomerRepository(),
                new Mock<EvangelionERPV2.Shared.Repositories.IRepository<Bill>>().Object,
                mockProductService.Object,
                null!,
                orderReportGeneratorService,
                mockHubContext.Object);

            var result = await orderService.CreateAsync(order);

            Assert.NotNull(result);
            mockGroupProxy.Verify(
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

            repositoryMock
                .Setup(r => r.ExecuteInTransaction(It.IsAny<Func<EvangelionERPV2.OrderModule.Application.Interface.OrderUpdateResult>>(), It.IsAny<CancellationToken>()))
                .Returns((Func<EvangelionERPV2.OrderModule.Application.Interface.OrderUpdateResult> operation, CancellationToken _) => operation());
        }

        [Fact]
        public async Task CreateAsync_SnapshotsTheProductCostOntoEachLine()
        {
            var enterpriseId = Guid.NewGuid();
            var costedProductId = Guid.NewGuid();
            var uncostedProductId = Guid.NewGuid();

            var order = new Order(DateTime.Now, DateTime.Now.AddDays(1), 0, enterpriseId, Guid.NewGuid(),
                new List<OrderedProduct>
                {
                    new() { IsActive = true, Quantity = 2, Value = 50, ProductId = costedProductId },
                    new() { IsActive = true, Quantity = 1, Value = 30, ProductId = uncostedProductId }
                }, Guid.NewGuid());

            _mockIProductRepository
                .Setup(r => r.GetAllAsync(It.IsAny<Expression<Func<Product, bool>>?>()))
                .ReturnsAsync(new List<Product>
                {
                    new() { Id = costedProductId, EnterpriseId = enterpriseId, IsActive = true, Name = "Costed", UnitOfMeasure = "UN", StorageQuantity = 100, CostPrice = 18.50m },
                    new() { Id = uncostedProductId, EnterpriseId = enterpriseId, IsActive = true, Name = "Uncosted", UnitOfMeasure = "UN", StorageQuantity = 100, CostPrice = null }
                });

            _mockIOrderRepository.Setup(r => r.CreateAsync(It.IsAny<Order>())).ReturnsAsync(order);

            var created = await _orderService.CreateAsync(order);

            var lines = created.OrderedProduct!.ToList();
            Assert.Equal(18.50m, lines.Single(x => x.ProductId == costedProductId).UnitCost);
            Assert.Null(lines.Single(x => x.ProductId == uncostedProductId).UnitCost);

            Assert.Null(OrderMargin.TotalCost(lines));
        }

        private static void SetupProductLookupForOrder(
            Mock<EvangelionERPV2.Shared.Repositories.IRepository<Product>> productRepositoryMock,
            Order order)
        {
            var requestedProducts = (order.OrderedProduct ?? Enumerable.Empty<OrderedProduct>())
                .Select(item => new Product
                {
                    Id = item.ProductId,
                    EnterpriseId = order.EnterpriseId,
                    IsActive = true,
                    Name = "Mock Product",
                    UnitOfMeasure = "UN",
                    StorageQuantity = 100
                })
                .ToList();

            productRepositoryMock
                .Setup(r => r.GetAllAsync(It.IsAny<Expression<Func<Product, bool>>?>()))
                .ReturnsAsync((Expression<Func<Product, bool>>? predicate) =>
                {
                    IEnumerable<Product> query = requestedProducts;
                    if (predicate != null)
                        query = query.Where(predicate.Compile());
                    return query;
                });
        }
        #endregion
    }
}
