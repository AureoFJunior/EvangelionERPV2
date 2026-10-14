using EvangelionERPV2.Shared.Configs;
using EvangelionERPV2.Shared.DTOs;
using EvangelionERPV2.Shared.Entities;

namespace EvangelionERPV2.Test.Bills
{
    /// <summary>A purchased variant is named with its variant, not only its family.</summary>
    public class PayableBillProductMappingTests
    {
        private static readonly AutoMapper.IMapper Mapper = MapperConfig.RegisterMaps().CreateMapper();

        [Fact]
        public void AVariantIsNamedWithItsVariantLabel()
        {
            var item = new PayableBillProduct { Product = new Product { Name = "Monster", VariantLabel = "Mango Loco" } };

            Assert.Equal("Monster — Mango Loco", Mapper.Map<PayableBillProductDTO>(item).ProductName);
        }

        [Fact]
        public void AStandaloneProductKeepsItsName()
        {
            var item = new PayableBillProduct { Product = new Product { Name = "Caneca" } };

            Assert.Equal("Caneca", Mapper.Map<PayableBillProductDTO>(item).ProductName);
        }
    }
}
