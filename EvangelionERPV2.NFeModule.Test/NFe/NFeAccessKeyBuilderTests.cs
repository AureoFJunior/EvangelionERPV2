using EvangelionERPV2.NFeModule.Infra.Xml;

namespace EvangelionERPV2.NFeModule.Test.NFe
{
    public class NFeAccessKeyBuilderTests
    {
        // cUF 35 | AAMM 2601 | CNPJ 12345678000195 | mod 55 | serie 001 | nNF 000000123 |
        // tpEmis 1 | cNF 12345678 -> 43-digit body, mod-11 sum 633, 633 % 11 = 6, DV = 11 - 6 = 5.
        private const string KnownBody = "3526011234567800019555001000000123112345678";
        private const string KnownKey = KnownBody + "5";

        private static string BuildKnown(string codigoNumerico = "12345678") =>
            NFeAccessKeyBuilder.Build(
                codigoUf: 35,
                emission: new DateTime(2026, 1, 15, 10, 30, 0),
                cnpj: "12345678000195",
                model: 55,
                series: 1,
                number: 123,
                tipoEmissao: 1,
                codigoNumerico: codigoNumerico);

        [Fact]
        public void Build_KnownInputs_ProducesExpected44DigitKeyWithHandComputedCheckDigit()
        {
            var key = BuildKnown();

            Assert.Equal(44, key.Length);
            Assert.Equal(KnownKey, key);
            Assert.True(NFeAccessKeyBuilder.IsValid(key));
        }

        [Fact]
        public void Build_AcceptsMaskedCnpj_AndZeroPadsEveryNumericField()
        {
            var key = NFeAccessKeyBuilder.Build(
                codigoUf: 41,
                emission: new DateTime(2026, 12, 31),
                cnpj: "12.345.678/0001-95",
                model: 65,
                series: 7,
                number: 9,
                tipoEmissao: 1,
                codigoNumerico: "00000042");

            Assert.StartsWith("41" + "2612" + "12345678000195" + "65" + "007" + "000000009" + "1" + "00000042", key);
            Assert.Equal(44, key.Length);
            Assert.True(NFeAccessKeyBuilder.IsValid(key));
        }

        // Both mod-11 boundaries collapse to DV 0. Derived from the KnownBody sum of 633: dropping
        // the last digit leaves 617, so sum = 617 + 2 * lastDigit.
        // lastDigit 5 -> 627, 627 % 11 = 0. lastDigit 0 -> 617, 617 % 11 = 1.
        [Theory]
        [InlineData("12345675")] // rest == 0
        [InlineData("12345670")] // rest == 1
        public void Build_CheckDigitBoundaries_ProduceZero(string codigoNumerico)
        {
            var key = BuildKnown(codigoNumerico);

            Assert.Equal('0', key[43]);
            Assert.True(NFeAccessKeyBuilder.IsValid(key));
        }

        [Theory]
        [InlineData(0)]
        [InlineData(100)]
        public void Build_CodigoUfOutOfRange_Throws(int codigoUf)
        {
            var ex = Assert.Throws<ArgumentException>(() => NFeAccessKeyBuilder.Build(
                codigoUf, new DateTime(2026, 1, 1), "12345678000195", 55, 1, 123, 1, "12345678"));

            Assert.Equal("codigoUf", ex.ParamName);
        }

        [Theory]
        [InlineData("1234567800019")]   // 13 digits
        [InlineData("123456780001955")] // 15 digits
        [InlineData("")]
        public void Build_CnpjWithWrongDigitCount_Throws(string cnpj)
        {
            var ex = Assert.Throws<ArgumentException>(() => NFeAccessKeyBuilder.Build(
                35, new DateTime(2026, 1, 1), cnpj, 55, 1, 123, 1, "12345678"));

            Assert.Equal("cnpj", ex.ParamName);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(56)]
        [InlineData(66)]
        public void Build_ModelOtherThan55Or65_Throws(int model)
        {
            var ex = Assert.Throws<ArgumentException>(() => NFeAccessKeyBuilder.Build(
                35, new DateTime(2026, 1, 1), "12345678000195", model, 1, 123, 1, "12345678"));

            Assert.Equal("model", ex.ParamName);
        }

        [Theory]
        [InlineData(-1)]
        [InlineData(1000)]
        public void Build_SeriesOutOfRange_Throws(int series)
        {
            var ex = Assert.Throws<ArgumentException>(() => NFeAccessKeyBuilder.Build(
                35, new DateTime(2026, 1, 1), "12345678000195", 55, series, 123, 1, "12345678"));

            Assert.Equal("series", ex.ParamName);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(1_000_000_000)]
        public void Build_NumberOutOfRange_Throws(int number)
        {
            var ex = Assert.Throws<ArgumentException>(() => NFeAccessKeyBuilder.Build(
                35, new DateTime(2026, 1, 1), "12345678000195", 55, 1, number, 1, "12345678"));

            Assert.Equal("number", ex.ParamName);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(10)]
        public void Build_TipoEmissaoOutOfRange_Throws(int tipoEmissao)
        {
            var ex = Assert.Throws<ArgumentException>(() => NFeAccessKeyBuilder.Build(
                35, new DateTime(2026, 1, 1), "12345678000195", 55, 1, 123, tipoEmissao, "12345678"));

            Assert.Equal("tipoEmissao", ex.ParamName);
        }

        [Theory]
        [InlineData("1234567")]  // 7 digits
        [InlineData("123456789")] // 9 digits
        [InlineData("1234567a")]
        [InlineData("")]
        [InlineData(null)]
        public void Build_CodigoNumericoNotEightDigits_Throws(string? codigoNumerico)
        {
            var ex = Assert.Throws<ArgumentException>(() => NFeAccessKeyBuilder.Build(
                35, new DateTime(2026, 1, 1), "12345678000195", 55, 1, 123, 1, codigoNumerico!));

            Assert.Equal("codigoNumerico", ex.ParamName);
        }

        [Fact]
        public void Build_CodigoNumericoEqualToNumber_Throws()
        {
            var ex = Assert.Throws<ArgumentException>(() => NFeAccessKeyBuilder.Build(
                35, new DateTime(2026, 1, 1), "12345678000195", 55, 1, 123, 1, "00000123"));

            Assert.Equal("codigoNumerico", ex.ParamName);
            Assert.Contains("cNF must not equal nNF", ex.Message);
        }

        [Fact]
        public void IsValid_SingleFlippedDigit_IsRejected()
        {
            // Flip one digit inside the body; the mod-11 DV no longer matches.
            var tampered = KnownKey.Remove(20, 1).Insert(20, KnownKey[20] == '9' ? "8" : "9");

            Assert.NotEqual(KnownKey, tampered);
            Assert.Equal(44, tampered.Length);
            Assert.False(NFeAccessKeyBuilder.IsValid(tampered));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("3526011234567800019555001000000123112345678")]   // 43 digits
        [InlineData("352601123456780001955500100000012311234567855")] // 45 digits
        [InlineData("3526011234567800019555001000000123112345678X")]  // non-digit
        public void IsValid_MalformedKey_IsRejected(string? accessKey)
        {
            Assert.False(NFeAccessKeyBuilder.IsValid(accessKey));
        }

        [Fact]
        public void GenerateCodigoNumerico_IsEightDigitsAndNeverEqualsNumber()
        {
            for (var i = 0; i < 200; i++)
            {
                var cnf = NFeAccessKeyBuilder.GenerateCodigoNumerico(7);

                Assert.Equal(8, cnf.Length);
                Assert.All(cnf, ch => Assert.True(char.IsAsciiDigit(ch)));
                Assert.NotEqual(7, int.Parse(cnf));
            }
        }

        [Fact]
        public void GenerateCodigoNumerico_FeedsBuildAndProducesAValidKey()
        {
            var cnf = NFeAccessKeyBuilder.GenerateCodigoNumerico(123);

            Assert.True(NFeAccessKeyBuilder.IsValid(BuildKnown(cnf)));
        }

        /// <summary>
        /// Cross-check against an independent implementation. This chave was emitted by
        /// Unimake.DFe 20260722.1716.27 itself during the phase-0 feasibility gate, for the inputs
        /// below, and its cDV was 8. A hand-computed vector only proves we agree with our own
        /// reading of the spec; this one proves we agree with the library that will actually sign
        /// the note. If this test ever goes red, our mod-11 or our field ordering has drifted.
        /// </summary>
        [Fact]
        public void Build_MatchesChaveGeneratedByUnimakeForSameInputs()
        {
            const string unimakeChave = "35260712345678000195550010000000011323801728";

            var actual = NFeAccessKeyBuilder.Build(
                codigoUf: 35,
                emission: new DateTime(2026, 7, 30, 12, 0, 0),
                cnpj: "12345678000195",
                model: 55,
                series: 1,
                number: 1,
                tipoEmissao: 1,
                codigoNumerico: "32380172");

            Assert.Equal(unimakeChave, actual);
            Assert.True(NFeAccessKeyBuilder.IsValid(actual));
        }
    }
}
