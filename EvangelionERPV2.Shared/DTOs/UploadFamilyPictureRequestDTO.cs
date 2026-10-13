namespace EvangelionERPV2.Shared.DTOs
{
    public sealed class UploadFamilyPictureRequestDTO
    {
        public Guid VariantGroupId { get; set; }
        public string File { get; set; } = string.Empty;
    }
}
