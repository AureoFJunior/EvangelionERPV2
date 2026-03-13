using EvangelionERPV2.Shared.Enums;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;

namespace EvangelionERPV2.Shared.Entities
{
    public class Product : BaseEntity
    {
        public Product() { }

        public Product(string name, string description, double defaultValue, double storageQuantity, bool isExternal, bool isService, string? pictureAdress, Guid enterpriseId)
        {
            Name = name;
            Description = description;
            DefaultValue = defaultValue;
            StorageQuantity = storageQuantity;
            IsExternal = isExternal;
            IsService = isService;
            PictureAdress = pictureAdress;
            EnterpriseId = enterpriseId;
        }

        public string Name { get; set; } = "";
        public string Description { get; set; } = "";
        public double DefaultValue { get; set; } = 0;
        public double StorageQuantity { get; set; } = 0;
        public string UnitOfMeasure { get; set; } = nameof(EnumUnitOfMeasure.Unit);
        public bool IsExternal { get; set; } = false;
        public bool IsService { get; set; } = false;
        public string? PictureAdress { get; set; }

        [ForeignKey(nameof(Enterprise))]
        public Guid? EnterpriseId { get; set; } = null;

        [JsonIgnore]
        public virtual Enterprise? Enterprise { get; set; } = null;

        [JsonIgnore]
        public virtual IEnumerable<OrderedProduct>? OrderedProduct { get; set; } = new List<OrderedProduct>();

        [JsonIgnore]
        public virtual IEnumerable<PayableBillProduct>? PayableBillProducts { get; set; } = new List<PayableBillProduct>();
    }
}
