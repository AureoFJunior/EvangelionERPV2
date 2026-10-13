using EvangelionERPV2.Shared.DTOs;
using EvangelionERPV2.Shared.Enums;
using EvangelionERPV2.Web.FluentValidator;

namespace EvangelionERPV2.Test.Security
{
    /// <summary>
    /// <c>NFeFiscalSettingsService</c> re-validates all of this server-side, so these assert the
    /// field-level answer a client gets rather than the last line of defence.
    /// </summary>
    public class UpdateFiscalSettingsRequestValidatorTests
    {
        private static EnterpriseFiscalSettingsDTO Settings() => new()
        {
            Cnpj = "11222333000181",
            InscricaoEstadual = "1234567890",
            RazaoSocial = "Empresa LTDA",
            Crt = 3,
            DefaultCst = "00",
            Logradouro = "Rua Barao de Teffe",
            Numero = "273",
            Bairro = "Menino Deus",
            Municipio = "Porto Alegre",
            CodigoMunicipioIbge = "4314902",
            Uf = "RS",
            Cep = "90160150",
            Environment = 2,
            NFeSeries = "1",
            NFCeSeries = "1",
            DefaultCfopIntra = "5102",
            DefaultCfopInter = "6102",
            DefaultOrigem = 0,
            NFeCancellationWindowMinutes = 1440,
            NFCeCancellationWindowMinutes = 30
        };

        [Fact]
        public void Validate_WithAWholeSettingsRow_IsValid()
        {
            var result = new UpdateFiscalSettingsRequestValidator().Validate(Settings());

            Assert.True(result.IsValid, string.Join("; ", result.Errors.Select(e => e.ErrorMessage)));
        }

        [Theory]
        [InlineData("11222333000182")]
        [InlineData("00000000000000")]
        [InlineData("")]
        public void Validate_WithAnInvalidCnpj_IsRefused(string cnpj)
        {
            var settings = Settings();
            settings.Cnpj = cnpj;

            var result = new UpdateFiscalSettingsRequestValidator().Validate(settings);

            Assert.False(result.IsValid);
            Assert.Contains(result.Errors, e => e.ErrorMessage.Contains("valid CNPJ"));
        }

        [Theory]
        [InlineData("SP")]
        [InlineData("rs")]
        [InlineData(" sc ")]
        public void Validate_WithASupportedUfInAnyCasing_IsValid(string uf)
        {
            var settings = Settings();
            settings.Uf = uf;

            Assert.True(new UpdateFiscalSettingsRequestValidator().Validate(settings).IsValid);
        }

        /// <summary>
        /// The list is what the provider is wired for, not what Brazil has. An unsupported UF has
        /// to be refused here rather than produce a rejection from an autorizador later.
        /// </summary>
        [Theory]
        [InlineData("MG")]
        [InlineData("")]
        [InlineData("   ")]
        public void Validate_WithAnUnsupportedUf_IsRefused(string uf)
        {
            var settings = Settings();
            settings.Uf = uf;

            var result = new UpdateFiscalSettingsRequestValidator().Validate(settings);

            Assert.False(result.IsValid);
            Assert.Contains(result.Errors, e => e.ErrorMessage.Contains("SP, RS, SC or PR"));
        }

        [Theory]
        [InlineData((byte)0)]
        [InlineData((byte)5)]
        public void Validate_WithACrtOutsideOneToFour_IsRefused(byte crt)
        {
            var settings = Settings();
            settings.Crt = crt;

            Assert.False(new UpdateFiscalSettingsRequestValidator().Validate(settings).IsValid);
        }

        /// <summary>Regime Normal declares a CST; Simples declares a CSOSN. Neither substitutes.</summary>
        [Fact]
        public void Validate_WithRegimeNormalAndNoCst_IsRefused()
        {
            var settings = Settings();
            settings.Crt = 3;
            settings.DefaultCst = null;

            var result = new UpdateFiscalSettingsRequestValidator().Validate(settings);

            Assert.False(result.IsValid);
            Assert.Contains(result.Errors, e => e.ErrorMessage.Contains("Regime Normal"));
        }

        [Theory]
        [InlineData((byte)1)]
        [InlineData((byte)2)]
        public void Validate_WithSimplesAndNoCsosn_IsRefused(byte crt)
        {
            var settings = Settings();
            settings.Crt = crt;
            settings.DefaultCst = null;
            settings.DefaultCsosn = null;

            var result = new UpdateFiscalSettingsRequestValidator().Validate(settings);

            Assert.False(result.IsValid);
            Assert.Contains(result.Errors, e => e.ErrorMessage.Contains("Simples Nacional"));
        }

        [Fact]
        public void Validate_WithSimplesAndACsosn_IsValid()
        {
            var settings = Settings();
            settings.Crt = 1;
            settings.DefaultCst = null;
            settings.DefaultCsosn = "102";

            Assert.True(new UpdateFiscalSettingsRequestValidator().Validate(settings).IsValid);
        }

        /// <summary>The digit count is taken after punctuation is stripped, so a formatted CEP passes.</summary>
        [Theory]
        [InlineData("90160-150")]
        [InlineData("90160150")]
        public void Validate_WithAFormattedOrBareCep_IsValid(string cep)
        {
            var settings = Settings();
            settings.Cep = cep;

            Assert.True(new UpdateFiscalSettingsRequestValidator().Validate(settings).IsValid);
        }

        [Theory]
        [InlineData("9016015")]
        [InlineData("901601500")]
        [InlineData("")]
        public void Validate_WithACepThatIsNotEightDigits_IsRefused(string cep)
        {
            var settings = Settings();
            settings.Cep = cep;

            Assert.False(new UpdateFiscalSettingsRequestValidator().Validate(settings).IsValid);
        }

        [Theory]
        [InlineData("431490")]
        [InlineData("43149021")]
        public void Validate_WithAnIbgeCodeThatIsNotSevenDigits_IsRefused(string code)
        {
            var settings = Settings();
            settings.CodigoMunicipioIbge = code;

            Assert.False(new UpdateFiscalSettingsRequestValidator().Validate(settings).IsValid);
        }

        [Theory]
        [InlineData("510")]
        [InlineData("51022")]
        public void Validate_WithACfopThatIsNotFourDigits_IsRefused(string cfop)
        {
            var settings = Settings();
            settings.DefaultCfopIntra = cfop;

            Assert.False(new UpdateFiscalSettingsRequestValidator().Validate(settings).IsValid);
        }

        [Theory]
        [InlineData((byte)0)]
        [InlineData((byte)3)]
        public void Validate_WithAnEnvironmentThatIsNotProducaoOrHomologacao_IsRefused(byte environment)
        {
            var settings = Settings();
            settings.Environment = environment;

            var result = new UpdateFiscalSettingsRequestValidator().Validate(settings);

            Assert.False(result.IsValid);
            Assert.Contains(result.Errors, e => e.ErrorMessage.Contains("producao"));
        }

        /// <summary>
        /// The aliquotas are rates, not percentages — 18% is 0.18. A value above 1 is the most
        /// likely paste error and the one that would silently multiply a nota's tax by a hundred.
        /// </summary>
        [Fact]
        public void Validate_WithAnAliquotaAboveOne_IsRefused()
        {
            var settings = Settings();
            settings.DefaultAliquotaIcms = 18m;

            Assert.False(new UpdateFiscalSettingsRequestValidator().Validate(settings).IsValid);
        }

        [Theory]
        [InlineData((byte)9)]
        [InlineData((byte)200)]
        public void Validate_WithAnOrigemOutsideZeroToEight_IsRefused(byte origem)
        {
            var settings = Settings();
            settings.DefaultOrigem = origem;

            Assert.False(new UpdateFiscalSettingsRequestValidator().Validate(settings).IsValid);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(1441)]
        public void Validate_WithACancellationWindowOutsideADay_IsRefused(int minutes)
        {
            var settings = Settings();
            settings.NFeCancellationWindowMinutes = minutes;

            Assert.False(new UpdateFiscalSettingsRequestValidator().Validate(settings).IsValid);
        }

        [Fact]
        public void Validate_WithoutASeries_IsRefused()
        {
            var settings = Settings();
            settings.NFeSeries = string.Empty;
            settings.NFCeSeries = string.Empty;

            var result = new UpdateFiscalSettingsRequestValidator().Validate(settings);

            Assert.False(result.IsValid);
            Assert.Equal(2, result.Errors.Count);
        }
    }

    public class FiscalRequestValidatorTests
    {
        /// <summary>SEFAZ fixes the justificativa at 15–255 characters; the edge restates it.</summary>
        [Theory]
        [InlineData(15)]
        [InlineData(255)]
        public void NFeCancel_WithAReasonInsideSefazsBounds_IsValid(int length)
        {
            Assert.True(new NFeCancelRequestValidator()
                .Validate(new NFeCancelRequestDTO { Reason = new string('m', length) }).IsValid);
        }

        [Theory]
        [InlineData(14)]
        [InlineData(256)]
        public void NFeCancel_WithAReasonOutsideSefazsBounds_IsRefused(int length)
        {
            Assert.False(new NFeCancelRequestValidator()
                .Validate(new NFeCancelRequestDTO { Reason = new string('m', length) }).IsValid);
        }

        [Fact]
        public void NFeCancel_WithNoReason_IsRefused()
        {
            Assert.False(new NFeCancelRequestValidator()
                .Validate(new NFeCancelRequestDTO { Reason = string.Empty }).IsValid);
        }

        [Fact]
        public void UpdateNFCeCsc_WithAnIdAndACsc_IsValid()
        {
            Assert.True(new UpdateNFCeCscRequestValidator()
                .Validate(new UpdateNFCeCscRequestDTO { CscId = 1, Csc = "ABC123" }).IsValid);
        }

        [Fact]
        public void UpdateNFCeCsc_WithoutAnId_IsRefused()
        {
            Assert.False(new UpdateNFCeCscRequestValidator()
                .Validate(new UpdateNFCeCscRequestDTO { CscId = 0, Csc = "ABC123" }).IsValid);
        }

        [Fact]
        public void UpdateNFCeCsc_WithAnOversizedCsc_IsRefused()
        {
            Assert.False(new UpdateNFCeCscRequestValidator()
                .Validate(new UpdateNFCeCscRequestDTO { CscId = 1, Csc = new string('c', 65) }).IsValid);
        }

        [Fact]
        public void SettleBill_WithAnAmountAndNoDate_IsValid()
        {
            Assert.True(new SettleBillRequestValidator()
                .Validate(new SettleBillRequestDTO { PaidAmount = 100m }).IsValid);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(-1)]
        public void SettleBill_WithNothingPaid_IsRefused(decimal amount)
        {
            Assert.False(new SettleBillRequestValidator()
                .Validate(new SettleBillRequestDTO { PaidAmount = amount }).IsValid);
        }

        /// <summary>
        /// The bound is the end of today, not this instant, so a terminal an hour ahead of the
        /// server can still settle a bill it took payment for.
        /// </summary>
        [Fact]
        public void SettleBill_LaterToday_IsValid()
        {
            var result = new SettleBillRequestValidator().Validate(new SettleBillRequestDTO
            {
                PaidAmount = 100m,
                PaidAt = DateTime.UtcNow.Date.AddHours(23)
            });

            Assert.True(result.IsValid);
        }

        [Fact]
        public void SettleBill_Tomorrow_IsRefused()
        {
            var result = new SettleBillRequestValidator().Validate(new SettleBillRequestDTO
            {
                PaidAmount = 100m,
                PaidAt = DateTime.UtcNow.Date.AddDays(1)
            });

            Assert.False(result.IsValid);
            Assert.Contains(result.Errors, e => e.ErrorMessage.Contains("in the future"));
        }

        [Fact]
        public void SettleBill_WithAnOversizedProtocol_IsRefused()
        {
            Assert.False(new SettleBillRequestValidator().Validate(new SettleBillRequestDTO
            {
                PaidAmount = 100m,
                SettlementProtocol = new string('p', 65)
            }).IsValid);
        }

        [Fact]
        public void Refund_WithoutAReason_IsRefused()
        {
            Assert.False(new RefundRequestValidator()
                .Validate(new RefundRequestDTO { Reason = string.Empty }).IsValid);
        }

        [Fact]
        public void Refund_WithAnOversizedReason_IsRefused()
        {
            Assert.False(new RefundRequestValidator()
                .Validate(new RefundRequestDTO { Reason = new string('r', 501) }).IsValid);
        }

        [Fact]
        public void CreateReport_WithAKnownType_IsValid()
        {
            Assert.True(new CreateReportRequestValidator()
                .Validate(new CreateReportRequestDTO { Type = EnumReportType.Stock }).IsValid);
        }

        [Fact]
        public void CreateReport_WithATypeOutsideTheEnum_IsRefused()
        {
            Assert.False(new CreateReportRequestValidator()
                .Validate(new CreateReportRequestDTO { Type = (EnumReportType)99 }).IsValid);
        }
    }

    public class UpsertPayableBillRequestValidatorTests
    {
        private static UpsertPayableBillRequestDTO Bill() => new()
        {
            Description = "Aluguel",
            BillType = (int)EnumPayableBillType.Accounts,
            DueDate = new DateTime(2026, 10, 10, 0, 0, 0, DateTimeKind.Utc),
            Amount = 2500m
        };

        [Fact]
        public void Validate_WithADescriptionAnAmountAndADueDate_IsValid()
        {
            Assert.True(new UpsertPayableBillRequestValidator().Validate(Bill()).IsValid);
        }

        [Fact]
        public void Validate_WithoutADueDate_IsRefused()
        {
            var bill = Bill();
            bill.DueDate = default;

            Assert.False(new UpsertPayableBillRequestValidator().Validate(bill).IsValid);
        }

        [Fact]
        public void Validate_WithAnUnknownBillType_IsRefused()
        {
            var bill = Bill();
            bill.BillType = 999;

            Assert.False(new UpsertPayableBillRequestValidator().Validate(bill).IsValid);
        }

        [Fact]
        public void Validate_WithNoItemsAndNoAmount_IsRefused()
        {
            var bill = Bill();
            bill.Amount = null;

            var result = new UpsertPayableBillRequestValidator().Validate(bill);

            Assert.False(result.IsValid);
            Assert.Contains(result.Errors, e => e.ErrorMessage.Contains("when no items"));
        }

        [Fact]
        public void Validate_WithNoItemsAndAZeroAmount_IsRefused()
        {
            var bill = Bill();
            bill.Amount = 0m;

            var result = new UpsertPayableBillRequestValidator().Validate(bill);

            Assert.False(result.IsValid);
            Assert.Contains(result.Errors, e => e.ErrorMessage.Contains("when no items"));
        }

        /// <summary>With items the amount is derived, so it does not have to be sent.</summary>
        [Fact]
        public void Validate_WithItemsAndNoAmount_IsValid()
        {
            var bill = Bill();
            bill.Amount = null;
            bill.Items = [new PayableBillItemRequestDTO { ProductId = Guid.NewGuid(), Quantity = 2, UnitValue = 10m }];

            Assert.True(new UpsertPayableBillRequestValidator().Validate(bill).IsValid);
        }

        [Fact]
        public void Validate_WithTheSameProductTwice_IsRefused()
        {
            var productId = Guid.NewGuid();
            var bill = Bill();
            bill.Items =
            [
                new PayableBillItemRequestDTO { ProductId = productId, Quantity = 1, UnitValue = 10m },
                new PayableBillItemRequestDTO { ProductId = productId, Quantity = 3, UnitValue = 10m }
            ];

            var result = new UpsertPayableBillRequestValidator().Validate(bill);

            Assert.False(result.IsValid);
            Assert.Contains(result.Errors, e => e.ErrorMessage.Contains("duplicate products"));
        }

        [Fact]
        public void Validate_WithAnItemAtZeroQuantity_IsRefused()
        {
            var bill = Bill();
            bill.Items = [new PayableBillItemRequestDTO { ProductId = Guid.NewGuid(), Quantity = 0, UnitValue = 10m }];

            Assert.False(new UpsertPayableBillRequestValidator().Validate(bill).IsValid);
        }

        [Fact]
        public void Validate_WithAnItemWithNoProduct_IsRefused()
        {
            var bill = Bill();
            bill.Items = [new PayableBillItemRequestDTO { ProductId = Guid.Empty, Quantity = 1, UnitValue = 10m }];

            Assert.False(new UpsertPayableBillRequestValidator().Validate(bill).IsValid);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(13)]
        public void Validate_WithARecurrenceOutsideOneToTwelveMonths_IsRefused(int months)
        {
            var bill = Bill();
            bill.RecurrenceMonths = months;
            bill.RecurrenceUntilUtc = bill.DueDate.AddYears(1);

            Assert.False(new UpsertPayableBillRequestValidator().Validate(bill).IsValid);
        }

        /// <summary>
        /// This one threw rather than failing: FluentValidation runs every component of a rule by
        /// default, so the date comparison dereferenced the null <c>NotNull</c> had just reported
        /// and the endpoint answered 500 to a body it should have answered 400 to.
        /// </summary>
        [Fact]
        public void Validate_WithARecurrenceAndNoEndDate_IsRefusedRatherThanThrowing()
        {
            var bill = Bill();
            bill.RecurrenceMonths = 1;
            bill.RecurrenceUntilUtc = null;

            var result = new UpsertPayableBillRequestValidator().Validate(bill);

            Assert.False(result.IsValid);
            Assert.Contains(result.Errors, e => e.ErrorMessage.Contains("needs an end date"));
        }

        /// <summary>
        /// An end date on the first due date would generate exactly one bill, which is the
        /// non-recurring case wearing a recurrence.
        /// </summary>
        [Fact]
        public void Validate_WithARecurrenceEndingOnTheFirstDueDate_IsRefused()
        {
            var bill = Bill();
            bill.RecurrenceMonths = 1;
            bill.RecurrenceUntilUtc = bill.DueDate;

            var result = new UpsertPayableBillRequestValidator().Validate(bill);

            Assert.False(result.IsValid);
            Assert.Contains(result.Errors, e => e.ErrorMessage.Contains("after the first due date"));
        }

        [Fact]
        public void Validate_WithAWholeRecurrence_IsValid()
        {
            var bill = Bill();
            bill.RecurrenceMonths = 1;
            bill.RecurrenceUntilUtc = bill.DueDate.AddYears(1);

            Assert.True(new UpsertPayableBillRequestValidator().Validate(bill).IsValid);
        }

        [Fact]
        public void Validate_WithAnOversizedDescription_IsRefused()
        {
            var bill = Bill();
            bill.Description = new string('d', 301);

            Assert.False(new UpsertPayableBillRequestValidator().Validate(bill).IsValid);
        }
    }
}
