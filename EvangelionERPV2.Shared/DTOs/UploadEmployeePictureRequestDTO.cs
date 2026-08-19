namespace EvangelionERPV2.Shared.DTOs
{
    public sealed class UploadEmployeePictureRequestDTO
    {
        public Guid EmployeeId { get; set; }

        // Optional: an empty/absent file clears the stored picture.
        public string? File { get; set; }
    }
}
