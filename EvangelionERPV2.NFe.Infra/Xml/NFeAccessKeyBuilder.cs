using EvangelionERPV2.Shared.Utils;
using System.Globalization;
using System.Security.Cryptography;

namespace EvangelionERPV2.NFeModule.Infra.Xml
{
    /// <summary>
    /// Builds and validates the 44-digit NFe/NFCe chave de acesso:
    /// cUF(2) + AAMM(4) + CNPJ(14) + mod(2) + serie(3) + nNF(9) + tpEmis(1) + cNF(8) + cDV(1).
    ///
    /// Pure and offline on purpose. The chave has to exist before the first byte reaches SEFAZ —
    /// it is what makes a timed-out transmission recoverable instead of a duplicate emission
    /// on a retry. Every input is validated here because a malformed chave means a rejected note
    /// or, worse, a burnt number.
    /// </summary>
    public static class NFeAccessKeyBuilder
    {
        private const int KeyLength = 44;
        private const int BodyLength = 43;

        /// <param name="emission">
        /// Emission timestamp in the emitter's own timezone, not UTC — AAMM must match the
        /// competência SEFAZ sees, and a UTC instant crosses the month boundary three hours early
        /// in Brazil.
        /// </param>
        /// <param name="codigoNumerico">
        /// cNF, 8 digits. Taken as a parameter so callers can persist it and rebuild the identical
        /// chave on retry; use <see cref="GenerateCodigoNumerico"/> to mint one.
        /// </param>
        public static string Build(
            int codigoUf,
            DateTime emission,
            string cnpj,
            int model,
            int series,
            int number,
            int tipoEmissao,
            string codigoNumerico)
        {
            if (codigoUf is < 1 or > 99)
            {
                throw new ArgumentException($"cUF must be between 1 and 99, got {codigoUf}.", nameof(codigoUf));
            }

            var cnpjDigits = DocumentValidationHelper.NormalizeDocument(cnpj);
            if (cnpjDigits.Length != 14)
            {
                throw new ArgumentException($"CNPJ must contain exactly 14 digits, got {cnpjDigits.Length}.", nameof(cnpj));
            }

            if (model is not (55 or 65))
            {
                throw new ArgumentException($"Model must be 55 (NFe) or 65 (NFCe), got {model}.", nameof(model));
            }

            if (series is < 0 or > 999)
            {
                throw new ArgumentException($"Series must be between 0 and 999, got {series}.", nameof(series));
            }

            if (number is < 1 or > 999_999_999)
            {
                throw new ArgumentException($"nNF must be between 1 and 999999999, got {number}.", nameof(number));
            }

            if (tipoEmissao is < 1 or > 9)
            {
                throw new ArgumentException($"tpEmis must be between 1 and 9, got {tipoEmissao}.", nameof(tipoEmissao));
            }

            if (!IsDigits(codigoNumerico, 8))
            {
                throw new ArgumentException("cNF must be exactly 8 digits.", nameof(codigoNumerico));
            }

            if (int.Parse(codigoNumerico, CultureInfo.InvariantCulture) == number)
            {
                throw new ArgumentException($"cNF must not equal nNF ({number}).", nameof(codigoNumerico));
            }

            var body = string.Concat(
                codigoUf.ToString("D2", CultureInfo.InvariantCulture),
                emission.ToString("yyMM", CultureInfo.InvariantCulture),
                cnpjDigits,
                model.ToString("D2", CultureInfo.InvariantCulture),
                series.ToString("D3", CultureInfo.InvariantCulture),
                number.ToString("D9", CultureInfo.InvariantCulture),
                tipoEmissao.ToString("D1", CultureInfo.InvariantCulture),
                codigoNumerico);

            return body + CheckDigit(body);
        }

        /// <summary>
        /// Cryptographically random 8-digit cNF, never equal to <paramref name="number"/>.
        /// RandomNumberGenerator rather than Random: a predictable cNF makes another tenant's
        /// chave guessable, and the chave is the only credential needed to fetch a note's XML.
        /// </summary>
        public static string GenerateCodigoNumerico(int number)
        {
            int value;
            do
            {
                value = RandomNumberGenerator.GetInt32(0, 100_000_000);
            }
            while (value == number);

            return value.ToString("D8", CultureInfo.InvariantCulture);
        }

        /// <summary>Length, all-digits and mod-11 check-digit verification.</summary>
        public static bool IsValid(string? accessKey)
        {
            return IsDigits(accessKey, KeyLength)
                && accessKey![BodyLength] == CheckDigit(accessKey.AsSpan(0, BodyLength));
        }

        private static char CheckDigit(ReadOnlySpan<char> body)
        {
            var sum = 0;
            var weight = 2;

            for (var i = body.Length - 1; i >= 0; i--)
            {
                sum += (body[i] - '0') * weight;
                weight = weight == 9 ? 2 : weight + 1;
            }

            var rest = sum % 11;
            return (char)('0' + (rest is 0 or 1 ? 0 : 11 - rest));
        }

        private static bool IsDigits(string? value, int length)
        {
            if (value is null || value.Length != length)
            {
                return false;
            }

            foreach (var ch in value)
            {
                if (!char.IsAsciiDigit(ch))
                {
                    return false;
                }
            }

            return true;
        }
    }
}
