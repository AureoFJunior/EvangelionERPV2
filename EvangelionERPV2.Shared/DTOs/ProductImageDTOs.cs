namespace EvangelionERPV2.Shared.DTOs
{
    public sealed record ProductImageDTO(Guid Id, int Position, int? Width, int? Height);

    public sealed class MoveProductImageRequestDTO
    {
        public int Position { get; set; }
    }
}
