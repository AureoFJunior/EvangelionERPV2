using AutoMapper;
using EvangelionERPV2.Shared.DTOs;
using EvangelionERPV2.Shared.Entities;
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
                config.CreateMap<User, UserDTO>().ReverseMap();
                config.CreateMap<Enterprise, EnterpriseDTO>().ReverseMap();
                config.CreateMap<EnterprisePayrollSettings, EnterprisePayrollSettingsDTO>().ReverseMap();
                config.CreateMap<EnterpriseFiscalSettings, EnterpriseFiscalSettingsDTO>().ReverseMap();
                config.CreateMap<Order, OrderDTO>()
                    .ForMember(dest => dest.CustomerName, opt => opt.MapFrom(src => src.Customer != null ? src.Customer.Name : null))
                    .ReverseMap();
                config.CreateMap<Bill, BillDTO>()
                    // Overdue is derived from the clock, never stored — see Bill.StatusAsOf. Mapped
                    // here so every read surface agrees instead of each caller deciding.
                    .ForMember(dest => dest.Status, opt => opt.MapFrom(src => src.StatusAsOf(DateTime.UtcNow)))
                    .ReverseMap()
                    // Derived on the way out; it must never be written back onto the entity.
                    .ForMember(dest => dest.Status, opt => opt.Ignore());
                config.CreateMap<PayableBill, PayableBillDTO>().ReverseMap();
                config.CreateMap<PayableBillProduct, PayableBillProductDTO>()
                    .ForMember(dest => dest.ProductName, opt => opt.MapFrom(src => src.Product != null ? src.Product.Name : string.Empty))
                    .ReverseMap();
                config.CreateMap<NFeDocument, NFeDocumentDTO>()
                    .ForMember(dest => dest.DisplayStatus,
                        opt => opt.MapFrom(src => NFeDocumentDTO.ResolveDisplayStatus(src)))
                    .ReverseMap()
                    // Derived on the way out; it must never be written back onto the entity.
                    .ForSourceMember(src => src.DisplayStatus, opt => opt.DoNotValidate());
                config.CreateMap<Customer, CustomerDTO>().ReverseMap();
                config.CreateMap<Employee, EmployeeDTO>().ReverseMap();
                // One-way on purpose: the DTO has no Cid, so a ReverseMap would be a mapping that
                // can only ever blank the encrypted diagnosis code on the entity.
                config.CreateMap<EmployeeAbsence, EmployeeAbsenceDTO>();
                config.CreateMap<EmployeeVacationAccrual, EmployeeVacationAccrualDTO>();
                config.CreateMap<Supplier, SupplierDTO>().ReverseMap();
                config.CreateMap<Product, ProductDTO>().ReverseMap();
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
 
