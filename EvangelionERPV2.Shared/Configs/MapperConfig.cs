using AutoMapper;
using EvangelionERPV2.Shared.DTOs;
using EvangelionERPV2.Shared.Entities;

namespace EvangelionERPV2.Shared.Configs
{
    public class MapperConfig
    {
        public static MapperConfiguration RegisterMaps()
        {
            var mappingsConfigs = new MapperConfiguration(config =>
            {
                config.CreateMap<User, UserDTO>().ReverseMap();
                config.CreateMap<Enterprise, EnterpriseDTO>().ReverseMap();
                config.CreateMap<Order, OrderDTO>().ReverseMap();
                config.CreateMap<Customer, CustomerDTO>().ReverseMap();
                config.CreateMap<Product, ProductDTO>().ReverseMap();
            });

            return mappingsConfigs;
        }
    }
}
