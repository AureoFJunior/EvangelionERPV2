using Microsoft.AspNetCore.Http;

namespace EvangelionERPV2.Shared.DTOs
{
    /// <summary>
    /// Multipart body for the A1 certificate upload.
    /// </summary>
    public class UploadNFeCertificateRequestDTO
    {
        public IFormFile? File { get; set; }

        public string? Password { get; set; }
    }
}
