using EvangelionERPV2.Shared.Enums;

namespace EvangelionERPV2.Shared.Entities
{
    public class Product : BaseEntity
    {
        public Product() { }

        public Product(string name, string description, double defaultValue, double storageQuantity, bool isExternal, bool isService)
        {
            Name = name;
            Description = description;
            DefaultValue = defaultValue;
            StorageQuantity = storageQuantity;
            IsExternal = isExternal;
            IsService = isService;
        }

        public string Name { get; set; } = "";
        public string Description { get; set; } = "";
        public double DefaultValue { get; set; } = 0;
        public double StorageQuantity { get; set; } = 0;
        public string UnitOfMeasure { get; set; } = nameof(EnumUnitOfMeasure.Unit);
        public bool IsExternal { get; set; } = false;
        public bool IsService { get; set; } = false;
        public string? PictureAdress { get; set; }
        public virtual IEnumerable<OrderedProduct>? OrderedProduct { get; set; } = null;
    }
}