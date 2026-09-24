using EvangelionERPV2.Shared.DTOs;
using EvangelionERPV2.Shared.Enums;
using EvangelionERPV2.Web.FluentValidator;

namespace EvangelionERPV2.Test.Security
{
    /// <summary>
    /// The till is the one client that talks to this API without a browser in front of it, so its
    /// payloads are shaped by a device rather than by a form. These assert the edge refuses what
    /// <c>PdvService</c> would otherwise have to.
    /// </summary>
    public class PdvRequestValidatorTests
    {
        private static PdvSaleItemRequestDTO Item() =>
            new() { ProductId = Guid.NewGuid(), Quantity = 1, Value = 10m };

        private static PdvSalePaymentRequestDTO Payment() =>
            new() { Method = (int)EnumPaymentMethod.Cash, Amount = 10m };

        private static PdvSaleSyncRequestDTO Sale() => new()
        {
            ClientSaleId = Guid.NewGuid(),
            ClientSessionId = Guid.NewGuid(),
            TerminalId = "caixa-01",
            SoldAtLocal = DateTime.UtcNow,
            Items = [Item()],
            Payments = [Payment()]
        };

        [Fact]
        public void OpenCashSession_WithATerminalAndAFloat_IsValid()
        {
            var result = new OpenCashSessionRequestValidator().Validate(new OpenCashSessionRequestDTO
            {
                ClientSessionId = Guid.NewGuid(),
                TerminalId = "caixa-01",
                OpeningFloat = 150m,
                OpenedAt = DateTime.UtcNow
            });

            Assert.True(result.IsValid);
        }

        [Fact]
        public void OpenCashSession_WithoutAClientSessionId_IsRefused()
        {
            var result = new OpenCashSessionRequestValidator().Validate(new OpenCashSessionRequestDTO
            {
                ClientSessionId = Guid.Empty,
                TerminalId = "caixa-01"
            });

            Assert.False(result.IsValid);
            Assert.Contains(result.Errors, e => e.ErrorMessage.Contains("ClientSessionId"));
        }

        [Theory]
        [InlineData("")]
        [InlineData(null)]
        public void OpenCashSession_WithoutATerminal_IsRefused(string? terminalId)
        {
            var result = new OpenCashSessionRequestValidator().Validate(new OpenCashSessionRequestDTO
            {
                ClientSessionId = Guid.NewGuid(),
                TerminalId = terminalId!
            });

            Assert.False(result.IsValid);
        }

        [Fact]
        public void OpenCashSession_WithATerminalIdOverSixtyFourCharacters_IsRefused()
        {
            var result = new OpenCashSessionRequestValidator().Validate(new OpenCashSessionRequestDTO
            {
                ClientSessionId = Guid.NewGuid(),
                TerminalId = new string('t', 65)
            });

            Assert.False(result.IsValid);
        }

        [Fact]
        public void OpenCashSession_WithANegativeFloat_IsRefused()
        {
            var result = new OpenCashSessionRequestValidator().Validate(new OpenCashSessionRequestDTO
            {
                ClientSessionId = Guid.NewGuid(),
                TerminalId = "caixa-01",
                OpeningFloat = -1m
            });

            Assert.False(result.IsValid);
        }

        [Fact]
        public void CloseCashSession_WithAZeroCount_IsValid()
        {
            var result = new CloseCashSessionRequestValidator().Validate(new CloseCashSessionRequestDTO
            {
                ClientSessionId = Guid.NewGuid(),
                CountedAmount = 0m,
                ClosedAt = DateTime.UtcNow
            });

            Assert.True(result.IsValid);
        }

        [Fact]
        public void CloseCashSession_WithANegativeCount_IsRefused()
        {
            var result = new CloseCashSessionRequestValidator().Validate(new CloseCashSessionRequestDTO
            {
                ClientSessionId = Guid.NewGuid(),
                CountedAmount = -0.01m
            });

            Assert.False(result.IsValid);
        }

        [Theory]
        [InlineData((int)EnumCashMovementType.Sangria)]
        [InlineData((int)EnumCashMovementType.Suprimento)]
        public void CashMovement_WithASangriaOrASuprimento_IsValid(int type)
        {
            var result = new CashMovementRequestValidator().Validate(new CashMovementRequestDTO
            {
                ClientMovementId = Guid.NewGuid(),
                ClientSessionId = Guid.NewGuid(),
                Type = type,
                Amount = 50m,
                Reason = "Troco",
                OccurredAt = DateTime.UtcNow
            });

            Assert.True(result.IsValid);
        }

        [Theory]
        [InlineData((int)EnumCashMovementType.Opening)]
        [InlineData(0)]
        [InlineData(99)]
        public void CashMovement_WithAnythingButASangriaOrASuprimento_IsRefused(int type)
        {
            var result = new CashMovementRequestValidator().Validate(new CashMovementRequestDTO
            {
                ClientMovementId = Guid.NewGuid(),
                ClientSessionId = Guid.NewGuid(),
                Type = type,
                Amount = 50m,
                Reason = "Troco"
            });

            Assert.False(result.IsValid);
            Assert.Contains(result.Errors, e => e.ErrorMessage.Contains("sangria"));
        }

        [Fact]
        public void CashMovement_WithoutAReason_IsRefused()
        {
            var result = new CashMovementRequestValidator().Validate(new CashMovementRequestDTO
            {
                ClientMovementId = Guid.NewGuid(),
                ClientSessionId = Guid.NewGuid(),
                Type = (int)EnumCashMovementType.Sangria,
                Amount = 50m,
                Reason = null
            });

            Assert.False(result.IsValid);
        }

        [Fact]
        public void CashMovement_WithAZeroAmount_IsRefused()
        {
            var result = new CashMovementRequestValidator().Validate(new CashMovementRequestDTO
            {
                ClientMovementId = Guid.NewGuid(),
                ClientSessionId = Guid.NewGuid(),
                Type = (int)EnumCashMovementType.Sangria,
                Amount = 0m,
                Reason = "Nada"
            });

            Assert.False(result.IsValid);
        }

        [Fact]
        public void SaleSync_WithOneItemAndOnePayment_IsValid()
        {
            Assert.True(new PdvSaleSyncRequestValidator().Validate(Sale()).IsValid);
        }

        [Fact]
        public void SaleSync_WithNoItems_IsRefused()
        {
            var sale = Sale();
            sale.Items = [];

            var result = new PdvSaleSyncRequestValidator().Validate(sale);

            Assert.False(result.IsValid);
            Assert.Contains(result.Errors, e => e.ErrorMessage.Contains("at least one item"));
        }

        [Fact]
        public void SaleSync_WithMoreThanFiveHundredItems_IsRefused()
        {
            var sale = Sale();
            sale.Items = Enumerable.Range(0, 501).Select(_ => Item()).ToList();

            var result = new PdvSaleSyncRequestValidator().Validate(sale);

            Assert.False(result.IsValid);
            Assert.Contains(result.Errors, e => e.ErrorMessage.Contains("Too many items"));
        }

        [Fact]
        public void SaleSync_WithMoreThanTwentyPayments_IsRefused()
        {
            var sale = Sale();
            sale.Payments = Enumerable.Range(0, 21).Select(_ => Payment()).ToList();

            var result = new PdvSaleSyncRequestValidator().Validate(sale);

            Assert.False(result.IsValid);
            Assert.Contains(result.Errors, e => e.ErrorMessage.Contains("Too many payments"));
        }

        [Fact]
        public void SaleSync_WithNoPayments_IsRefused()
        {
            var sale = Sale();
            sale.Payments = [];

            Assert.False(new PdvSaleSyncRequestValidator().Validate(sale).IsValid);
        }

        [Fact]
        public void SaleSync_WithANegativeDiscountOrSurcharge_IsRefused()
        {
            var sale = Sale();
            sale.DiscountAmount = -1m;
            sale.SurchargeAmount = -1m;

            var result = new PdvSaleSyncRequestValidator().Validate(sale);

            Assert.False(result.IsValid);
            Assert.Equal(2, result.Errors.Count);
        }

        /// <summary>
        /// The item and payment validators run through <c>RuleForEach</c>, so a bad line has to
        /// fail the whole sale rather than be dropped from it.
        /// </summary>
        [Fact]
        public void SaleSync_WithAnItemAtZeroQuantity_IsRefused()
        {
            var sale = Sale();
            sale.Items = [new PdvSaleItemRequestDTO { ProductId = Guid.NewGuid(), Quantity = 0, Value = 10m }];

            Assert.False(new PdvSaleSyncRequestValidator().Validate(sale).IsValid);
        }

        [Fact]
        public void SaleItem_WithoutAProduct_IsRefused()
        {
            var result = new PdvSaleItemRequestValidator().Validate(
                new PdvSaleItemRequestDTO { ProductId = Guid.Empty, Quantity = 1, Value = 1m });

            Assert.False(result.IsValid);
        }

        [Fact]
        public void SaleItem_WithANegativeValue_IsRefused()
        {
            var result = new PdvSaleItemRequestValidator().Validate(
                new PdvSaleItemRequestDTO { ProductId = Guid.NewGuid(), Quantity = 1, Value = -0.01m });

            Assert.False(result.IsValid);
        }

        [Fact]
        public void Payment_WithAnUnknownMethod_IsRefused()
        {
            var result = new PdvSalePaymentRequestValidator().Validate(
                new PdvSalePaymentRequestDTO { Method = 77, Amount = 10m });

            Assert.False(result.IsValid);
            Assert.Contains(result.Errors, e => e.ErrorMessage.Contains("Unknown payment method"));
        }

        /// <summary>
        /// <c>Mixed</c> is an order-level summary, not a tender. A till sending it would produce a
        /// sale whose payments do not say how any of the money arrived.
        /// </summary>
        [Fact]
        public void Payment_WithMixed_IsRefused()
        {
            var result = new PdvSalePaymentRequestValidator().Validate(
                new PdvSalePaymentRequestDTO { Method = (int)EnumPaymentMethod.Mixed, Amount = 10m });

            Assert.False(result.IsValid);
            Assert.Contains(result.Errors, e => e.ErrorMessage.Contains("Mixed is not a tender"));
        }

        [Fact]
        public void Payment_WithAZeroAmount_IsRefused()
        {
            var result = new PdvSalePaymentRequestValidator().Validate(
                new PdvSalePaymentRequestDTO { Method = (int)EnumPaymentMethod.Cash, Amount = 0m });

            Assert.False(result.IsValid);
        }

        [Fact]
        public void Payment_GivingChangeOnCash_IsValid()
        {
            var result = new PdvSalePaymentRequestValidator().Validate(new PdvSalePaymentRequestDTO
            {
                Method = (int)EnumPaymentMethod.Cash,
                Amount = 10m,
                ChangeGiven = 2m
            });

            Assert.True(result.IsValid);
        }

        [Theory]
        [InlineData((int)EnumPaymentMethod.Debit)]
        [InlineData((int)EnumPaymentMethod.Credit)]
        [InlineData((int)EnumPaymentMethod.Pix)]
        public void Payment_GivingChangeOnAnythingButCash_IsRefused(int method)
        {
            var result = new PdvSalePaymentRequestValidator().Validate(new PdvSalePaymentRequestDTO
            {
                Method = method,
                Amount = 10m,
                ChangeGiven = 2m
            });

            Assert.False(result.IsValid);
            Assert.Contains(result.Errors, e => e.ErrorMessage.Contains("Only a cash payment"));
        }

        [Fact]
        public void Payment_WithNegativeChange_IsRefused()
        {
            var result = new PdvSalePaymentRequestValidator().Validate(new PdvSalePaymentRequestDTO
            {
                Method = (int)EnumPaymentMethod.Cash,
                Amount = 10m,
                ChangeGiven = -1m
            });

            Assert.False(result.IsValid);
        }

        [Fact]
        public void Payment_WithAnAuthorizationCodeOverSixtyFourCharacters_IsRefused()
        {
            var result = new PdvSalePaymentRequestValidator().Validate(new PdvSalePaymentRequestDTO
            {
                Method = (int)EnumPaymentMethod.Credit,
                Amount = 10m,
                AuthorizationCode = new string('a', 65)
            });

            Assert.False(result.IsValid);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(37)]
        public void Payment_WithInstallmentsOutsideOneToThirtySix_IsRefused(int installments)
        {
            var result = new PdvSalePaymentRequestValidator().Validate(new PdvSalePaymentRequestDTO
            {
                Method = (int)EnumPaymentMethod.Credit,
                Amount = 10m,
                Installments = installments
            });

            Assert.False(result.IsValid);
        }

        /// <summary>Null installments means "not in installments", not "zero of them".</summary>
        [Fact]
        public void Payment_WithNoInstallments_IsValid()
        {
            var result = new PdvSalePaymentRequestValidator().Validate(new PdvSalePaymentRequestDTO
            {
                Method = (int)EnumPaymentMethod.Credit,
                Amount = 10m,
                Installments = null
            });

            Assert.True(result.IsValid);
        }

        [Fact]
        public void SaleSyncBatch_WithNoSales_IsRefused()
        {
            var result = new PdvSaleSyncBatchRequestValidator().Validate(
                new PdvSaleSyncBatchRequestDTO { Sales = [] });

            Assert.False(result.IsValid);
        }

        [Fact]
        public void SaleSyncBatch_CarriesTheSaleRulesIntoEveryRow()
        {
            var bad = Sale();
            bad.Items = [];

            var result = new PdvSaleSyncBatchRequestValidator().Validate(
                new PdvSaleSyncBatchRequestDTO { Sales = [Sale(), bad] });

            Assert.False(result.IsValid);
            Assert.Contains(result.Errors, e => e.ErrorMessage.Contains("at least one item"));
        }

        [Fact]
        public void EnrollTerminal_WithoutATerminalId_IsRefused()
        {
            Assert.False(new EnrollTerminalRequestValidator()
                .Validate(new EnrollTerminalRequestDTO { TerminalId = string.Empty }).IsValid);
        }

        [Fact]
        public void EnrollTerminal_WithATerminalId_IsValid()
        {
            Assert.True(new EnrollTerminalRequestValidator()
                .Validate(new EnrollTerminalRequestDTO { TerminalId = "caixa-01" }).IsValid);
        }

        [Fact]
        public void PdvLogin_WithAUserAndAPassword_IsValid()
        {
            Assert.True(new PdvLoginRequestValidator()
                .Validate(new PdvLoginRequestDTO { UserName = "op", Password = "s3nh4" }).IsValid);
        }

        [Fact]
        public void PdvLogin_WithoutAPassword_IsRefused()
        {
            Assert.False(new PdvLoginRequestValidator()
                .Validate(new PdvLoginRequestDTO { UserName = "op", Password = string.Empty }).IsValid);
        }

        [Fact]
        public void PdvLogin_WithOversizedCredentials_IsRefused()
        {
            var result = new PdvLoginRequestValidator().Validate(new PdvLoginRequestDTO
            {
                UserName = new string('u', 257),
                Password = new string('p', 257)
            });

            Assert.False(result.IsValid);
            Assert.Equal(2, result.Errors.Count);
        }

        [Theory]
        [InlineData("123456")]
        [InlineData("123456789012")]
        public void SetPdvPin_WithSixToTwelveDigits_IsValid(string pin)
        {
            Assert.True(new SetPdvPinRequestValidator().Validate(new SetPdvPinRequestDTO { Pin = pin }).IsValid);
        }

        [Theory]
        [InlineData("12345")]
        [InlineData("1234567890123")]
        [InlineData("12345a")]
        [InlineData("")]
        public void SetPdvPin_WithAnythingElse_IsRefused(string pin)
        {
            Assert.False(new SetPdvPinRequestValidator().Validate(new SetPdvPinRequestDTO { Pin = pin }).IsValid);
        }

        /// <summary>
        /// <c>ClientSaleId</c> is deliberately not required: an override can happen with no basket
        /// open, and a rule demanding one would make those unreportable.
        /// </summary>
        [Fact]
        public void RegisterOverride_WithNoSaleOpen_IsValid()
        {
            var result = new RegisterOverrideRequestValidator().Validate(new RegisterOverrideRequestDTO
            {
                ClientOverrideId = Guid.NewGuid(),
                SupervisorUserId = Guid.NewGuid(),
                TerminalId = "caixa-01",
                ClientSaleId = Guid.Empty,
                Permission = "pdv.discount",
                Succeeded = false,
                OccurredAt = DateTime.UtcNow
            });

            Assert.True(result.IsValid);
        }

        [Fact]
        public void RegisterOverride_WithoutASupervisor_IsRefused()
        {
            var result = new RegisterOverrideRequestValidator().Validate(new RegisterOverrideRequestDTO
            {
                ClientOverrideId = Guid.NewGuid(),
                SupervisorUserId = Guid.Empty,
                TerminalId = "caixa-01",
                Permission = "pdv.discount"
            });

            Assert.False(result.IsValid);
        }

        [Fact]
        public void RegisterOverride_WithoutAPermission_IsRefused()
        {
            var result = new RegisterOverrideRequestValidator().Validate(new RegisterOverrideRequestDTO
            {
                ClientOverrideId = Guid.NewGuid(),
                SupervisorUserId = Guid.NewGuid(),
                TerminalId = "caixa-01",
                Permission = string.Empty
            });

            Assert.False(result.IsValid);
        }

        [Theory]
        [InlineData(0.50)]
        [InlineData(3000)]
        public void StartPixCharge_AtStripesBounds_IsValid(decimal amount)
        {
            var result = new StartPixChargeRequestValidator().Validate(new StartPixChargeRequestDTO
            {
                ClientSaleId = Guid.NewGuid(),
                Amount = amount
            });

            Assert.True(result.IsValid);
        }

        [Theory]
        [InlineData(0.49)]
        [InlineData(3000.01)]
        public void StartPixCharge_OutsideStripesBounds_IsRefused(decimal amount)
        {
            var result = new StartPixChargeRequestValidator().Validate(new StartPixChargeRequestDTO
            {
                ClientSaleId = Guid.NewGuid(),
                Amount = amount
            });

            Assert.False(result.IsValid);
        }

        [Fact]
        public void CancelPixCharge_WithoutAChargeId_IsRefused()
        {
            Assert.False(new CancelPixChargeRequestValidator()
                .Validate(new CancelPixChargeRequestDTO { ChargeId = Guid.Empty }).IsValid);
        }

        [Fact]
        public void CancelPixCharge_WithAChargeId_IsValid()
        {
            Assert.True(new CancelPixChargeRequestValidator()
                .Validate(new CancelPixChargeRequestDTO { ChargeId = Guid.NewGuid() }).IsValid);
        }
    }
}
