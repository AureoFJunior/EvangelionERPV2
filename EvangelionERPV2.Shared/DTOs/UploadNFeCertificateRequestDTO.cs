using Microsoft.AspNetCore.Http;

namespace EvangelionERPV2.Shared.DTOs
{
    /// <summary>
    /// Multipart body for the A1 certificate upload.
    /// </summary>
    /// <remarks>
    /// A single <c>[FromForm]</c> model rather than loose <c>IFormFile</c> + <c>string</c>
    /// parameters: Swashbuckle cannot map the loose form and throws
    /// <c>SwaggerGeneratorException</c> while generating the document, which takes the whole
    /// <c>/swagger/v1/swagger.json</c> endpoint down with a 500 — not just this one operation.
    /// The password is never logged and never echoed back.
    /// </remarks>
    public class UploadNFeCertificateRequestDTO
    {
        public IFormFile? File { get; set; }

        public string? Password { get; set; }
    }
}
