using System.Security.Cryptography;
using System.Text;

namespace EvangelionERPV2.UserModule.Application.Services
{
    /// <summary>
    /// RFC 6238 TOTP (HMAC-SHA1, 30 s step) plus the RFC 4648 base32 that authenticator apps read.
    /// </summary>
    public static class Totp
    {
        public const int StepSeconds = 30;
        private const int SecretBytes = 20;
        private const string Base32Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";

        public static byte[] NewSecret() => RandomNumberGenerator.GetBytes(SecretBytes);

        public static long CurrentStep(DateTimeOffset now) => now.ToUnixTimeSeconds() / StepSeconds;

        public static string Code(byte[] secret, long step, int digits = 6)
        {
            Span<byte> counter = stackalloc byte[8];
            System.Buffers.Binary.BinaryPrimitives.WriteInt64BigEndian(counter, step);
            Span<byte> hash = stackalloc byte[20];
            HMACSHA1.HashData(secret, counter, hash);

            var offset = hash[^1] & 0x0F;
            var binary = ((hash[offset] & 0x7F) << 24)
                         | (hash[offset + 1] << 16)
                         | (hash[offset + 2] << 8)
                         | hash[offset + 3];

            return (binary % (int)Math.Pow(10, digits)).ToString(System.Globalization.CultureInfo.InvariantCulture).PadLeft(digits, '0');
        }

        /// <summary>
        /// The step the code belongs to, searched one step either side of <paramref name="currentStep"/>,
        /// or null when it matches none or only steps at or before <paramref name="lastUsedStep"/>.
        /// </summary>
        public static long? MatchStep(byte[] secret, string? code, long currentStep, long? lastUsedStep)
        {
            var digits = (code ?? string.Empty).Replace(" ", string.Empty);
            if (digits.Length != 6 || !digits.All(char.IsAsciiDigit))
                return null;

            var supplied = Encoding.ASCII.GetBytes(digits);
            long? matched = null;
            for (var step = currentStep - 1; step <= currentStep + 1; step++)
            {
                var expected = Encoding.ASCII.GetBytes(Code(secret, step));
                if (CryptographicOperations.FixedTimeEquals(expected, supplied) && (lastUsedStep is null || step > lastUsedStep))
                    matched = step;
            }

            return matched;
        }

        public static string ToBase32(byte[] data)
        {
            var output = new StringBuilder((data.Length + 4) / 5 * 8);
            for (var i = 0; i < data.Length; i += 5)
            {
                var chunk = data.Skip(i).Take(5).ToArray();
                ulong buffer = 0;
                for (var j = 0; j < 5; j++)
                    buffer = (buffer << 8) | (j < chunk.Length ? chunk[j] : (byte)0);

                var chars = (chunk.Length * 8 + 4) / 5;
                for (var j = 0; j < 8; j++)
                    output.Append(j < chars ? Base32Alphabet[(int)((buffer >> (35 - j * 5)) & 0x1F)] : '=');
            }

            return output.ToString();
        }

        public static byte[] FromBase32(string text)
        {
            var bytes = new List<byte>();
            int buffer = 0, bits = 0;
            foreach (var c in text.TrimEnd('=').ToUpperInvariant())
            {
                var value = Base32Alphabet.IndexOf(c);
                if (value < 0)
                    throw new FormatException("Invalid base32 character.");

                buffer = (buffer << 5) | value;
                bits += 5;
                if (bits >= 8)
                {
                    bits -= 8;
                    bytes.Add((byte)(buffer >> bits));
                    buffer &= (1 << bits) - 1;
                }
            }

            return bytes.ToArray();
        }

        public static string NewRecoveryCode()
        {
            var code = RandomNumberGenerator.GetString(Base32Alphabet, 10);
            return $"{code[..5]}-{code[5..]}";
        }

        /// <summary>The ten base32 characters of a recovery code, or null when the input is not one.</summary>
        public static string? NormalizeRecoveryCode(string? input)
        {
            var code = (input ?? string.Empty).Replace("-", string.Empty).Replace(" ", string.Empty).ToUpperInvariant();
            return code.Length == 10 && code.All(c => Base32Alphabet.Contains(c)) ? code : null;
        }
    }
}
