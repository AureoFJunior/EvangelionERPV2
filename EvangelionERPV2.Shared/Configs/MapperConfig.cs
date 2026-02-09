using AutoMapper;
using EvangelionERPV2.Shared.DTOs;
using EvangelionERPV2.Shared.Entities;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Extensions.Logging;

namespace EvangelionERPV2.Shared.Configs
{
    public class MapperConfig
    {
        public static MapperConfiguration RegisterMaps()
        {
            ILoggerFactory loggerFactory = new SerilogLoggerFactory(Log.Logger, dispose: false);

            var mappingsConfigs = new MapperConfiguration(config =>
            {
                config.CreateMap<User, UserDTO>().ReverseMap();
                config.CreateMap<Enterprise, EnterpriseDTO>().ReverseMap();
                config.CreateMap<Order, OrderDTO>()
                    .ForMember(dest => dest.CustomerName, opt => opt.MapFrom(src => src.Customer != null ? src.Customer.Name : null))
                    .ReverseMap();
                config.CreateMap<Bill, BillDTO>().ReverseMap();
                config.CreateMap<NFeDocument, NFeDocumentDTO>().ReverseMap();
                config.CreateMap<Customer, CustomerDTO>().ReverseMap();
                config.CreateMap<Product, ProductDTO>().ReverseMap();
            }, loggerFactory);

            return mappingsConfigs;
        }
    }
}
 
