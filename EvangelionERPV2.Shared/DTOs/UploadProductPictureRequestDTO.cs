namespace EvangelionERPV2.Shared.DTOs
{
    public sealed class UploadProductPictureRequestDTO
    {
        public Guid ProductId { get; set; }
        public string File { get; set; } = string.Empty;
    }
}
