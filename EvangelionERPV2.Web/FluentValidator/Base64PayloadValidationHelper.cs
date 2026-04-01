using EvangelionERPV2.Shared.Utils;

namespace EvangelionERPV2.Web.FluentValidator
{
    public static class Base64PayloadValidationHelper
    {
        private static readonly byte[] PngSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
        private static readonly byte[] JpegSignaturePrefix = [0xFF, 0xD8, 0xFF];
        private static readonly byte[] Gif87aSignature = [0x47, 0x49, 0x46, 0x38, 0x37, 0x61];
        private static readonly byte[] Gif89aSignature = [0x47, 0x49, 0x46, 0x38, 0x39, 0x61];
        private static readonly byte[] WebpRiffSignature = [0x52, 0x49, 0x46, 0x46];
        private static readonly byte[] WebpSignature = [0x57, 0x45, 0x42, 0x50];

        public static bool IsValidBase64Payload(string? payload)
        {
            return TryGetDecodedByteCount(payload, out var decodedByteCount) && decodedByteCount > 0;
        }

        public static bool IsWithinDecodedSizeLimit(string? payload, long maxBytes)
        {
            if (maxBytes <= 0)
                return false;

            return TryGetDecodedByteCount(payload, out var decodedByteCount) && decodedByteCount <= maxBytes;
        }

        public static bool HasSupportedImageSignature(string? payload)
        {
            if (!TryDecodePayload(payload, out var decodedBytes))
                return false;

            return decodedBytes.AsSpan().StartsWith(PngSignature) ||
                   decodedBytes.AsSpan().StartsWith(JpegSignaturePrefix) ||
                   decodedBytes.AsSpan().StartsWith(Gif87aSignature) ||
                   decodedBytes.AsSpan().StartsWith(Gif89aSignature) ||
                   HasWebpSignature(decodedBytes);
        }

        public static bool TryGetDecodedByteCount(string? payload, out long decodedByteCount)
        {
            decodedByteCount = 0;

            var normalizedPayload = SharedFunctions.NormalizeBase64Payload(payload ?? string.Empty);
            if (string.IsNullOrWhiteSpace(normalizedPayload))
                return false;

            var length = 0;
            var paddingCount = 0;
            var seenPadding = false;

            foreach (var character in normalizedPayload)
            {
                if (char.IsWhiteSpace(character))
                    continue;

                if (character == '=')
                {
                    seenPadding = true;
                    paddingCount++;
                    length++;

                    if (paddingCount > 2)
                        return false;

                    continue;
                }

                if (seenPadding || !IsBase64Character(character))
                    return false;

                length++;
            }

            if (length == 0 || length % 4 != 0)
                return false;

            decodedByteCount = ((long)length / 4 * 3) - paddingCount;
            return decodedByteCount > 0;
        }

        private static bool TryDecodePayload(string? payload, out byte[] decodedBytes)
        {
            decodedBytes = Array.Empty<byte>();

            var normalizedPayload = SharedFunctions.NormalizeBase64Payload(payload ?? string.Empty);
            if (string.IsNullOrWhiteSpace(normalizedPayload))
                return false;

            try
            {
                decodedBytes = Convert.FromBase64String(normalizedPayload);
                return decodedBytes.Length > 0;
            }
            catch (FormatException)
            {
                return false;
            }
        }

        private static bool HasWebpSignature(ReadOnlySpan<byte> decodedBytes)
        {
            if (decodedBytes.Length < 12)
                return false;

            return decodedBytes[..4].SequenceEqual(WebpRiffSignature) &&
                   decodedBytes[8..12].SequenceEqual(WebpSignature);
        }

        private static bool IsBase64Character(char value)
        {
            return (value >= 'A' && value <= 'Z') ||
                   (value >= 'a' && value <= 'z') ||
                   (value >= '0' && value <= '9') ||
                   value == '+' ||
                   value == '/';
        }
    }
}
