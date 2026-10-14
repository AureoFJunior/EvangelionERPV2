namespace EvangelionERPV2.Web.FluentValidator
{
    internal static class DocumentValidationHelper
    {
        public static bool BeValidDocument(string? document)
        {
            if (string.IsNullOrWhiteSpace(document))
            {
                return true;
            }

            var digits = NormalizeDocument(document);
            return IsValidCpf(digits) || IsValidCnpj(digits) || IsValidNif(digits);
        }

        private static string NormalizeDocument(string value)
        {
            return new string(value.Where(char.IsDigit).ToArray());
        }

        private static bool IsValidCpf(string digits)
        {
            if (digits.Length != 11)
            {
                return false;
            }

            if (digits.Distinct().Count() == 1)
            {
                return false;
            }

            var numbers = digits.Select(ch => ch - '0').ToArray();
            var sum = 0;

            for (var i = 0; i < 9; i++)
            {
                sum += numbers[i] * (10 - i);
            }

            var remainder = sum % 11;
            var firstDigit = remainder < 2 ? 0 : 11 - remainder;
            if (numbers[9] != firstDigit)
            {
                return false;
            }

            sum = 0;
            for (var i = 0; i < 10; i++)
            {
                sum += numbers[i] * (11 - i);
            }

            remainder = sum % 11;
            var secondDigit = remainder < 2 ? 0 : 11 - remainder;
            return numbers[10] == secondDigit;
        }

        private static bool IsValidCnpj(string digits)
        {
            if (digits.Length != 14)
            {
                return false;
            }

            if (digits.Distinct().Count() == 1)
            {
                return false;
            }

            var numbers = digits.Select(ch => ch - '0').ToArray();
            var weights1 = new[] { 5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2 };
            var weights2 = new[] { 6, 5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2 };

            var sum = 0;
            for (var i = 0; i < 12; i++)
            {
                sum += numbers[i] * weights1[i];
            }

            var remainder = sum % 11;
            var firstDigit = remainder < 2 ? 0 : 11 - remainder;
            if (numbers[12] != firstDigit)
            {
                return false;
            }

            sum = 0;
            for (var i = 0; i < 13; i++)
            {
                sum += numbers[i] * weights2[i];
            }

            remainder = sum % 11;
            var secondDigit = remainder < 2 ? 0 : 11 - remainder;
            return numbers[13] == secondDigit;
        }

        private static bool IsValidNif(string digits)
        {
            if (digits.Length != 9)
            {
                return false;
            }

            var first = digits[0];
            if (!"1235689".Contains(first))
            {
                return false;
            }

            var numbers = digits.Select(ch => ch - '0').ToArray();
            var sum = 0;

            for (var i = 0; i < 8; i++)
            {
                sum += numbers[i] * (9 - i);
            }

            var remainder = sum % 11;
            var checkDigit = remainder < 2 ? 0 : 11 - remainder;
            return numbers[8] == checkDigit;
        }
    }
}
