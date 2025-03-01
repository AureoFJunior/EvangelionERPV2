using EvangelionERPV2.ProductModule.Application.Interface;
using EvangelionERPV2.ProductModule.Domain.Interface;
using EvangelionERPV2.Shared.Entities;
using EvangelionERPV2.Shared.Exceptions;

namespace EvangelionERPV2.ProductModule.Application.Services
{
    public class OrderedProductService : IOrderedProductService<OrderedProduct>
    {
        private readonly IRepository<OrderedProduct> _orderedProductRepository;

        public OrderedProductService(IRepository<OrderedProduct> orderedProductRepository)
        {
            _orderedProductRepository = orderedProductRepository;
        }

        public async Task<OrderedProduct> CreateAsync(OrderedProduct orderedProduct)
        {
            try
            {
                var existentOrderedProduct = _orderedProductRepository.GetById(orderedProduct.Id);
                OrderedProduct includedOrderedProduct = new OrderedProduct();

                if (existentOrderedProduct != null)
                    throw new InsertDatabaseException($"{nameof(OrderedProduct)} already has an register in database");

                includedOrderedProduct = await _orderedProductRepository.CreateAsync(orderedProduct);
                await _orderedProductRepository.CommitAsync();
                return includedOrderedProduct;
            }
            catch (Exception ex)
            {
                throw new InsertDatabaseException(ex.Message, ex.InnerException);
            }
        }

        public OrderedProduct Update(OrderedProduct orderedProduct)
        {
            try
            {
                OrderedProduct existentOrderedProduct = _orderedProductRepository.GetById(orderedProduct.Id);
                OrderedProduct updatedOrderedProduct = new OrderedProduct();

                if (existentOrderedProduct == null)
                    throw new NotFoundDatabaseException($"{nameof(OrderedProduct)} was not found in database.");

                orderedProduct.UpdatedAt = DateTime.UtcNow;
                updatedOrderedProduct = _orderedProductRepository.Update(orderedProduct);
                _orderedProductRepository.Commit();
                return updatedOrderedProduct;
            }
            catch (NotFoundDatabaseException ex)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new InsertDatabaseException(ex.Message, ex.InnerException);
            }
        }

        public OrderedProduct Delete(Guid id)
        {
            try
            {
                OrderedProduct orderedOrderedProduct = _orderedProductRepository.GetById(id);
                OrderedProduct deletedOrderedProduct = new OrderedProduct();

                if (orderedOrderedProduct == null)
                    throw new NotFoundDatabaseException($"{nameof(OrderedProduct)} was not found in database.");

                orderedOrderedProduct.IsActive = false;
                orderedOrderedProduct.UpdatedAt = DateTime.UtcNow;
                deletedOrderedProduct = _orderedProductRepository.Update(orderedOrderedProduct);
                _orderedProductRepository.Commit();
                return deletedOrderedProduct;
            }
            catch (NotFoundDatabaseException ex)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new InsertDatabaseException(ex.Message, ex.InnerException);
            }
        }
    }
}