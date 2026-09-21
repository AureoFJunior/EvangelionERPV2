namespace EvangelionERPV2.Shared.DTOs
{
    public sealed class UploadEmployeePictureRequestDTO
    {
        public Guid EmployeeId { get; set; }

        public string? File { get; set; }
    }
}
