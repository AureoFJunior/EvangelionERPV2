using AutoMapper;

namespace EvangelionERPV2.Application.Configs
{
    public class MapperConfig
    {
        public static MapperConfiguration RegisterMaps()
        {
            var mappingsConfigs = new MapperConfiguration(config =>
            {
                //config.CreateMap<Roles, RolesDTO>().ReverseMap();
            });

            return mappingsConfigs;
        }
    }
}
