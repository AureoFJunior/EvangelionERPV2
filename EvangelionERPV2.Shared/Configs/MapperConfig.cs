using AutoMapper;
using EvangelionERPV2.Shared.DTOs;
using EvangelionERPV2.Shared.Entities;
using EvangelionERPV2.Shared.Enums;
using EvangelionERPV2.Shared.Utils;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Extensions.Logging;
using System.Security.Cryptography;
using System.Text;

namespace EvangelionERPV2.Shared.Configs
{
    public class MapperConfig
    {
        public static MapperConfiguration RegisterMaps()
        {
            ILoggerFactory loggerFactory = new SerilogLoggerFactory(Log.Logger, dispose: false);

            var mappingsConfigs = new MapperConfiguration(config =>
            {
                config.CreateMap<User, UserDTO>()
                    .ForMember(dto => dto.AppleLinked, opt => opt.MapFrom(user => user.AppleSubject != null))
                    .ReverseMap();
                config.CreateMap<Enterprise, EnterpriseDTO>().ReverseMap();
                config.CreateMap<EnterprisePayrollSettings, EnterprisePayrollSettingsDTO>().ReverseMap();
                config.CreateMap<EnterpriseFiscalSettings, EnterpriseFiscalSettingsDTO>().ReverseMap();
                config.CreateMap<Order, OrderDTO>()
                    .ForMember(dest => dest.CustomerName, opt => opt.MapFrom(src =>
                        src.Customer != null ? src.Customer.Name
                        : src.CustomerId == null ? "Venda PDV"
                        : null))
                    .ForMember(dest => dest.TotalCost, opt => opt.MapFrom(src => OrderMargin.TotalCost(src.OrderedProduct)))
                    .ForMember(dest => dest.MarginAmount, opt => opt.MapFrom(src => OrderMargin.Amount(src.TotalValue, OrderMargin.TotalCost(src.OrderedProduct))))
                    .ForMember(dest => dest.MarginPercent, opt => opt.MapFrom(src => OrderMargin.Percent(src.TotalValue, OrderMargin.TotalCost(src.OrderedProduct))))
                    .ForMember(dest => dest.DeliveryAddress, opt => opt.MapFrom((src, _) => OrderDeliveryAddressDTO.Parse(src.DeliveryAddress)))
                    .ForMember(dest => dest.StorefrontOrderNumber, opt => opt.MapFrom((src, _) =>
                        src.Channel == (int)EnumOrderChannel.Storefront ? StorefrontOrderNumber.For(src.Id) : null))
                    .ReverseMap()
                    .ForMember(dest => dest.DeliveryAddress, opt => opt.Ignore())
                    .ForMember(dest => dest.Channel, opt => opt.Ignore())
                    .ForMember(dest => dest.ShippingValue, opt => opt.Ignore())
                    .ForMember(dest => dest.ShippedAt, opt => opt.Ignore())
                    .ForMember(dest => dest.DeliveredAt, opt => opt.Ignore());
                config.CreateMap<Bill, BillDTO>()
                    .ForMember(dest => dest.Status, opt => opt.MapFrom(src => src.StatusAsOf(DateTime.UtcNow)))
                    .ReverseMap()
                    .ForMember(dest => dest.Status, opt => opt.Ignore());
                config.CreateMap<PayableBill, PayableBillDTO>().ReverseMap();
                config.CreateMap<PayableBillProduct, PayableBillProductDTO>()
                    // A variant reads "Name — Variant", as everywhere else; the family name alone does not say which was bought.
                    .ForMember(dest => dest.ProductName, opt => opt.MapFrom(src => src.Product == null ? string.Empty
                        : string.IsNullOrEmpty(src.Product.VariantLabel) ? src.Product.Name
                        : src.Product.Name + " — " + src.Product.VariantLabel))
                    .ReverseMap();
                config.CreateMap<NFeDocument, NFeDocumentDTO>()
                    .ForMember(dest => dest.DisplayStatus,
                        opt => opt.MapFrom(src => NFeDocumentDTO.ResolveDisplayStatus(src)))
                    .ReverseMap()
                    .ForSourceMember(src => src.DisplayStatus, opt => opt.DoNotValidate());
                config.CreateMap<Customer, CustomerDTO>().ReverseMap();
                config.CreateMap<Employee, EmployeeDTO>().ReverseMap();
                config.CreateMap<EmployeeAbsence, EmployeeAbsenceDTO>();
                config.CreateMap<EmployeeVacationAccrual, EmployeeVacationAccrualDTO>();
                config.CreateMap<Supplier, SupplierDTO>().ReverseMap();
                config.CreateMap<Product, ProductDTO>()
                    .ForMember(dest => dest.MarginPercent, opt => opt.MapFrom(src => OrderMargin.UnitPercent(src.DefaultValue, src.CostPrice)))
                    .ReverseMap();
                config.CreateMap<AuditTrail, AuditTrailDTO>()
                    .ForMember(dest => dest.UserName,
                        opt => opt.MapFrom(src => GetSafeAuditTrailUserIdentifier(src.User == null ? null : src.User.UserName)));
                config.CreateMap<Opportunity, OpportunityDTO>().ReverseMap();
                config.CreateMap<OpportunitySignal, OpportunitySignalDTO>().ReverseMap();
                config.CreateMap<OpportunityRecommendation, OpportunityRecommendationDTO>().ReverseMap();
                config.CreateMap<OpportunityFeedback, OpportunityFeedbackDTO>().ReverseMap();
                config.CreateMap<OpportunityRunLog, OpportunityRunLogDTO>().ReverseMap();
            }, loggerFactory);

            return mappingsConfigs;
        }

        private static string GetSafeAuditTrailUserIdentifier(string? userName)
        {
            var normalizedUserName = (userName ?? string.Empty).Trim().ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(normalizedUserName))
                return string.Empty;

            var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(normalizedUserName));
            return Convert.ToHexString(bytes).ToLowerInvariant()[..12];
        }
    }
}
 
