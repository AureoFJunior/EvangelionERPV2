using EvangelionERPV2.Shared.DTOs;
using EvangelionERPV2.Shared.Enums;
using EvangelionERPV2.Web.FluentValidator;

namespace EvangelionERPV2.Test.Security
{
    public class EnterpriseRequestValidatorTests
    {
        private static CreateEnterpriseRequestDTO NewEnterprise() => new()
        {
            Name = "Empresa LTDA",
            Email = "contato@example.com",
            PhoneNumber = "+55 51 3333-0000",
            Adress = "Rua Barao de Teffe, 273"
        };

        [Fact]
        public void CreateEnterprise_WithEveryRequiredField_IsValid()
        {
            Assert.True(new CreateEnterpriseRequestValidator().Validate(NewEnterprise()).IsValid);
        }

        [Fact]
        public void CreateEnterprise_WithNothing_IsRefusedOnEveryRequiredField()
        {
            var result = new CreateEnterpriseRequestValidator().Validate(new CreateEnterpriseRequestDTO());

            Assert.False(result.IsValid);
            Assert.Equal(
                ["Adress", "Email", "Name", "PhoneNumber"],
                result.Errors.Select(e => e.PropertyName).Distinct().Order());
        }

        /// <summary>Currency is optional, and only checked when something was sent.</summary>
        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("BRL")]
        public void CreateEnterprise_WithNoCurrencyOrAnIsoCode_IsValid(string? currency)
        {
            var request = NewEnterprise();
            request.Currency = currency;

            Assert.True(new CreateEnterpriseRequestValidator().Validate(request).IsValid);
        }

        [Theory]
        [InlineData("BR")]
        [InlineData("BRLL")]
        public void CreateEnterprise_WithACurrencyThatIsNotThreeLetters_IsRefused(string currency)
        {
            var request = NewEnterprise();
            request.Currency = currency;

            Assert.False(new CreateEnterpriseRequestValidator().Validate(request).IsValid);
        }

        [Fact]
        public void UpdateEnterprise_WithoutAnId_IsRefused()
        {
            var result = new UpdateEnterpriseRequestValidator().Validate(new UpdateEnterpriseRequestDTO
            {
                Id = Guid.Empty,
                Name = "Empresa LTDA",
                Email = "contato@example.com",
                PhoneNumber = "+55 51 3333-0000",
                Adress = "Rua Barao de Teffe, 273"
            });

            Assert.False(result.IsValid);
        }

        [Fact]
        public void UpdateEnterprise_WithAnId_IsValid()
        {
            var result = new UpdateEnterpriseRequestValidator().Validate(new UpdateEnterpriseRequestDTO
            {
                Id = Guid.NewGuid(),
                Name = "Empresa LTDA",
                Email = "contato@example.com",
                PhoneNumber = "+55 51 3333-0000",
                Adress = "Rua Barao de Teffe, 273"
            });

            Assert.True(result.IsValid);
        }

        /// <summary>
        /// Closing a period locks every movement through it. Closing through a future date would
        /// lock work nobody has done yet.
        /// </summary>
        [Fact]
        public void ClosePeriod_Tomorrow_IsRefused()
        {
            var result = new ClosePeriodRequestValidator().Validate(
                new ClosePeriodRequestDTO { ClosedThroughUtc = DateTime.UtcNow.Date.AddDays(1) });

            Assert.False(result.IsValid);
        }

        [Fact]
        public void ClosePeriod_Today_IsValid()
        {
            var result = new ClosePeriodRequestValidator().Validate(
                new ClosePeriodRequestDTO { ClosedThroughUtc = DateTime.UtcNow.Date });

            Assert.True(result.IsValid);
        }

        /// <summary>A null date means "reopen", which the future-date rule must not fire on.</summary>
        [Fact]
        public void ClosePeriod_WithNoDate_IsValid()
        {
            Assert.True(new ClosePeriodRequestValidator().Validate(new ClosePeriodRequestDTO()).IsValid);
        }
    }

    public class QuoteRequestValidatorTests
    {
        private static UpsertQuoteRequestDTO Quote() => new()
        {
            CustomerId = Guid.NewGuid(),
            Items = [new QuoteItemRequestDTO { ProductId = Guid.NewGuid(), Quantity = 1, UnitValue = 10m }]
        };

        [Fact]
        public void UpsertQuote_WithOneItem_IsValid()
        {
            Assert.True(new UpsertQuoteRequestValidator().Validate(Quote()).IsValid);
        }

        [Fact]
        public void UpsertQuote_WithNoItems_IsRefused()
        {
            var quote = Quote();
            quote.Items = [];

            Assert.False(new UpsertQuoteRequestValidator().Validate(quote).IsValid);
        }

        [Fact]
        public void UpsertQuote_WithMoreThanTwoHundredItems_IsRefused()
        {
            var quote = Quote();
            quote.Items = Enumerable.Range(0, 201)
                .Select(_ => new QuoteItemRequestDTO { ProductId = Guid.NewGuid(), Quantity = 1, UnitValue = 1m })
                .ToList();

            Assert.False(new UpsertQuoteRequestValidator().Validate(quote).IsValid);
        }

        [Fact]
        public void UpsertQuote_WithANegativeQuotedPrice_IsRefused()
        {
            var quote = Quote();
            quote.Items = [new QuoteItemRequestDTO { ProductId = Guid.NewGuid(), Quantity = 1, UnitValue = -1m }];

            var result = new UpsertQuoteRequestValidator().Validate(quote);

            Assert.False(result.IsValid);
            Assert.Contains(result.Errors, e => e.ErrorMessage.Contains("cannot be negative"));
        }

        /// <summary>A quote that expired before it was written is a data-entry slip, not a quote.</summary>
        [Fact]
        public void UpsertQuote_WithAValidityInThePast_IsRefused()
        {
            var quote = Quote();
            quote.ValidUntil = DateTime.UtcNow.Date.AddDays(-1);

            Assert.False(new UpsertQuoteRequestValidator().Validate(quote).IsValid);
        }

        [Fact]
        public void UpsertQuote_ValidUntilToday_IsValid()
        {
            var quote = Quote();
            quote.ValidUntil = DateTime.UtcNow.Date;

            Assert.True(new UpsertQuoteRequestValidator().Validate(quote).IsValid);
        }

        [Fact]
        public void UpsertQuote_WithAnOversizedNote_IsRefused()
        {
            var quote = Quote();
            quote.Notes = new string('n', 1001);

            Assert.False(new UpsertQuoteRequestValidator().Validate(quote).IsValid);
        }

        [Theory]
        [InlineData((int)EnumQuoteStatus.Sent)]
        [InlineData((int)EnumQuoteStatus.Accepted)]
        [InlineData((int)EnumQuoteStatus.Rejected)]
        public void QuoteStatus_WithATransitionThisEndpointOwns_IsValid(int status)
        {
            Assert.True(new QuoteStatusRequestValidator()
                .Validate(new QuoteStatusRequestDTO { Status = status }).IsValid);
        }

        /// <summary>
        /// Draft is where a quote starts, not somewhere this endpoint may send it back to — items
        /// can change in Draft, and reopening one that was already sent would rewrite history.
        /// </summary>
        [Theory]
        [InlineData((int)EnumQuoteStatus.Draft)]
        [InlineData(0)]
        [InlineData(99)]
        public void QuoteStatus_WithAnythingElse_IsRefused(int status)
        {
            Assert.False(new QuoteStatusRequestValidator()
                .Validate(new QuoteStatusRequestDTO { Status = status }).IsValid);
        }

        [Fact]
        public void ConvertQuote_WithNoPaymentDate_IsValid()
        {
            Assert.True(new ConvertQuoteRequestValidator().Validate(new ConvertQuoteRequestDTO()).IsValid);
        }

        [Fact]
        public void ConvertQuote_WithAPaymentDateOverAYearBack_IsRefused()
        {
            var result = new ConvertQuoteRequestValidator().Validate(
                new ConvertQuoteRequestDTO { PaymentScheduledDate = DateTime.UtcNow.Date.AddYears(-1).AddDays(-1) });

            Assert.False(result.IsValid);
        }

        [Fact]
        public void ConvertQuote_WithAPaymentDateInsideTheLastYear_IsValid()
        {
            var result = new ConvertQuoteRequestValidator().Validate(
                new ConvertQuoteRequestDTO { PaymentScheduledDate = DateTime.UtcNow.Date.AddMonths(-1) });

            Assert.True(result.IsValid);
        }
    }

    public class SupplierRequestValidatorTests
    {
        private static CreateSupplierRequestDTO NewSupplier() => new()
        {
            Name = "Fornecedor LTDA",
            Email = "vendas@example.com",
            PhoneNumber = "+55 51 3333-0000",
            Adress = "Av. Ipiranga, 100"
        };

        [Fact]
        public void CreateSupplier_WithEveryRequiredField_IsValid()
        {
            Assert.True(new CreateSupplierRequestValidator().Validate(NewSupplier()).IsValid);
        }

        [Fact]
        public void CreateSupplier_WithAValidCnpj_IsValid()
        {
            var request = NewSupplier();
            request.Document = "11.222.333/0001-81";

            Assert.True(new CreateSupplierRequestValidator().Validate(request).IsValid);
        }

        [Fact]
        public void CreateSupplier_WithADocumentThatFailsItsCheckDigits_IsRefused()
        {
            var request = NewSupplier();
            request.Document = "11222333000182";

            var result = new CreateSupplierRequestValidator().Validate(request);

            Assert.False(result.IsValid);
            Assert.Contains(result.Errors, e => e.ErrorMessage.Contains("valid CPF, CNPJ, or NIF"));
        }

        [Fact]
        public void CreateSupplier_WithNoDocument_IsValid()
        {
            var request = NewSupplier();
            request.Document = null;

            Assert.True(new CreateSupplierRequestValidator().Validate(request).IsValid);
        }

        [Fact]
        public void UpdateSupplier_WithoutAnId_IsRefused()
        {
            var result = new UpdateSupplierRequestValidator().Validate(new UpdateSupplierRequestDTO
            {
                Id = Guid.Empty,
                Name = "Fornecedor LTDA",
                Email = "vendas@example.com",
                PhoneNumber = "+55 51 3333-0000",
                Adress = "Av. Ipiranga, 100"
            });

            Assert.False(result.IsValid);
        }
    }

    public class SalesGoalRequestValidatorTests
    {
        [Fact]
        public void UpsertSalesGoal_WithAMonthAndATarget_IsValid()
        {
            var result = new UpsertSalesGoalRequestValidator().Validate(new UpsertSalesGoalRequestDTO
            {
                UserId = Guid.NewGuid(),
                Year = 2026,
                Month = 9,
                TargetAmount = 50_000m
            });

            Assert.True(result.IsValid);
        }

        [Theory]
        [InlineData(1999, 9)]
        [InlineData(2101, 9)]
        [InlineData(2026, 0)]
        [InlineData(2026, 13)]
        public void UpsertSalesGoal_OutsideTheYearOrMonthBounds_IsRefused(int year, int month)
        {
            var result = new UpsertSalesGoalRequestValidator().Validate(new UpsertSalesGoalRequestDTO
            {
                Year = year,
                Month = month,
                TargetAmount = 1m
            });

            Assert.False(result.IsValid);
        }

        /// <summary>Zero is a legitimate target — a month nobody is expected to sell in.</summary>
        [Fact]
        public void UpsertSalesGoal_WithAZeroTarget_IsValid()
        {
            var result = new UpsertSalesGoalRequestValidator().Validate(new UpsertSalesGoalRequestDTO
            {
                Year = 2026,
                Month = 9,
                TargetAmount = 0m
            });

            Assert.True(result.IsValid);
        }

        [Theory]
        [InlineData(-1)]
        [InlineData(1_000_000_001)]
        public void UpsertSalesGoal_WithATargetOutsideZeroToABillion_IsRefused(decimal target)
        {
            var result = new UpsertSalesGoalRequestValidator().Validate(new UpsertSalesGoalRequestDTO
            {
                Year = 2026,
                Month = 9,
                TargetAmount = target
            });

            Assert.False(result.IsValid);
        }
    }

    public class CreateBroadcastRequestValidatorTests
    {
        private static CreateBroadcastDTO Broadcast() => new()
        {
            Title = "Manutenção no sábado",
            Body = "O sistema fica indisponível das 8h às 10h.",
            Severity = EnumNotificationSeverity.Warning
        };

        [Fact]
        public void Validate_WithATitleAndABody_IsValid()
        {
            Assert.True(new CreateBroadcastRequestValidator().Validate(Broadcast()).IsValid);
        }

        [Fact]
        public void Validate_WithNoTitleOrBody_IsRefused()
        {
            var result = new CreateBroadcastRequestValidator().Validate(new CreateBroadcastDTO());

            Assert.False(result.IsValid);
            Assert.Equal(2, result.Errors.Count);
        }

        [Fact]
        public void Validate_WithASeverityOutsideTheEnum_IsRefused()
        {
            var broadcast = Broadcast();
            broadcast.Severity = (EnumNotificationSeverity)9;

            Assert.False(new CreateBroadcastRequestValidator().Validate(broadcast).IsValid);
        }

        /// <summary>An expiry already past would create a broadcast nobody can ever see.</summary>
        [Fact]
        public void Validate_WithAnExpiryInThePast_IsRefused()
        {
            var broadcast = Broadcast();
            broadcast.ExpiresAt = DateTime.UtcNow.AddMinutes(-1);

            Assert.False(new CreateBroadcastRequestValidator().Validate(broadcast).IsValid);
        }

        [Fact]
        public void Validate_WithNoExpiry_IsValid()
        {
            var broadcast = Broadcast();
            broadcast.ExpiresAt = null;

            Assert.True(new CreateBroadcastRequestValidator().Validate(broadcast).IsValid);
        }
    }

    public class EmployeeRequestValidatorTests
    {
        private static CreateEmployeeRequestDTO NewEmployee() => new()
        {
            Name = "Maria",
            Email = "maria@example.com",
            PhoneNumber = "+55 51 99999-0000",
            Position = "Vendedora"
        };

        [Fact]
        public void CreateEmployee_WithEveryRequiredField_IsValid()
        {
            Assert.True(new CreateEmployeeRequestValidator().Validate(NewEmployee()).IsValid);
        }

        [Fact]
        public void CreateEmployee_WithNothing_IsRefusedOnEveryRequiredField()
        {
            var result = new CreateEmployeeRequestValidator().Validate(new CreateEmployeeRequestDTO());

            Assert.False(result.IsValid);
            Assert.Equal(
                ["Email", "Name", "PhoneNumber", "Position"],
                result.Errors.Select(e => e.PropertyName).Distinct().Order());
        }

        [Fact]
        public void CreateEmployee_WithANegativeSalary_IsRefused()
        {
            var request = NewEmployee();
            request.Salary = -1m;

            Assert.False(new CreateEmployeeRequestValidator().Validate(request).IsValid);
        }

        /// <summary>
        /// The bound is a day out rather than "now", so a client in a timezone ahead of the server
        /// can still hire somebody today.
        /// </summary>
        [Fact]
        public void CreateEmployee_HiredToday_IsValid()
        {
            var request = NewEmployee();
            request.HireDate = DateTime.UtcNow;

            Assert.True(new CreateEmployeeRequestValidator().Validate(request).IsValid);
        }

        [Fact]
        public void CreateEmployee_HiredNextWeek_IsRefused()
        {
            var request = NewEmployee();
            request.HireDate = DateTime.UtcNow.AddDays(7);

            Assert.False(new CreateEmployeeRequestValidator().Validate(request).IsValid);
        }

        [Fact]
        public void CreateEmployee_WithAPictureThatIsNotAnImage_IsRefused()
        {
            var request = NewEmployee();
            request.Picture = Convert.ToBase64String("plain text"u8.ToArray());

            Assert.False(new CreateEmployeeRequestValidator().Validate(request).IsValid);
        }

        [Fact]
        public void UpdateEmployee_TerminatedBeforeHired_IsRefused()
        {
            var result = new UpdateEmployeeRequestValidator().Validate(new UpdateEmployeeRequestDTO
            {
                Id = Guid.NewGuid(),
                Name = "Maria",
                Email = "maria@example.com",
                PhoneNumber = "+55 51 99999-0000",
                Position = "Vendedora",
                HireDate = new DateTime(2026, 5, 1, 0, 0, 0, DateTimeKind.Utc),
                TerminationDate = new DateTime(2026, 4, 1, 0, 0, 0, DateTimeKind.Utc)
            });

            Assert.False(result.IsValid);
            Assert.Contains(result.Errors, e => e.ErrorMessage.Contains("before the hire date"));
        }

        /// <summary>
        /// A termination with no hire date on record cannot be compared, and the rule must sit out
        /// rather than dereference the missing half.
        /// </summary>
        [Fact]
        public void UpdateEmployee_TerminatedWithNoHireDateOnRecord_IsValid()
        {
            var result = new UpdateEmployeeRequestValidator().Validate(new UpdateEmployeeRequestDTO
            {
                Id = Guid.NewGuid(),
                Name = "Maria",
                Email = "maria@example.com",
                PhoneNumber = "+55 51 99999-0000",
                Position = "Vendedora",
                HireDate = null,
                TerminationDate = new DateTime(2026, 4, 1, 0, 0, 0, DateTimeKind.Utc)
            });

            Assert.True(result.IsValid);
        }

        [Fact]
        public void EmployeeFilter_WithAnEmptyFilter_IsValid()
        {
            Assert.True(new EmployeeFilterRequestValidator().Validate(new EmployeeFilterRequestDTO()).IsValid);
        }

        [Fact]
        public void EmployeeFilter_WithOversizedTerms_IsRefused()
        {
            var result = new EmployeeFilterRequestValidator().Validate(new EmployeeFilterRequestDTO
            {
                Name = new string('n', 151),
                Email = new string('e', 151),
                Position = new string('p', 101),
                PhoneNumber = new string('9', 31)
            });

            Assert.False(result.IsValid);
            Assert.Equal(4, result.Errors.Count);
        }
    }

    public class EmployeeAbsenceRequestValidatorTests
    {
        private static CreateEmployeeAbsenceRequestDTO Absence() => new()
        {
            EmployeeId = Guid.NewGuid(),
            Type = (int)EnumEmployeeAbsenceType.Falta,
            StartDate = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc),
            EndDate = new DateTime(2026, 9, 2, 0, 0, 0, DateTimeKind.Utc)
        };

        [Fact]
        public void CreateAbsence_OverTwoDays_IsValid()
        {
            Assert.True(new CreateEmployeeAbsenceRequestValidator().Validate(Absence()).IsValid);
        }

        [Fact]
        public void CreateAbsence_EndingBeforeItStarts_IsRefused()
        {
            var absence = Absence();
            absence.EndDate = absence.StartDate.AddDays(-1);

            var result = new CreateEmployeeAbsenceRequestValidator().Validate(absence);

            Assert.False(result.IsValid);
            Assert.Contains(result.Errors, e => e.ErrorMessage.Contains("before the start date"));
        }

        [Fact]
        public void CreateAbsence_SpanningMoreThanAYear_IsRefused()
        {
            var absence = Absence();
            absence.EndDate = absence.StartDate.AddDays(367);

            var result = new CreateEmployeeAbsenceRequestValidator().Validate(absence);

            Assert.False(result.IsValid);
            Assert.Contains(result.Errors, e => e.ErrorMessage.Contains("more than a year"));
        }

        [Fact]
        public void CreateAbsence_WithoutAnEmployee_IsRefused()
        {
            var absence = Absence();
            absence.EmployeeId = Guid.Empty;

            Assert.False(new CreateEmployeeAbsenceRequestValidator().Validate(absence).IsValid);
        }

        [Fact]
        public void CreateAbsence_WithATypeOutsideTheEnum_IsRefused()
        {
            var absence = Absence();
            absence.Type = 999;

            Assert.False(new CreateEmployeeAbsenceRequestValidator().Validate(absence).IsValid);
        }

        /// <summary>
        /// A CID is health data under the LGPD. It belongs on an atestado and nowhere else, so the
        /// rule is about what may be stored rather than about the field's shape.
        /// </summary>
        [Fact]
        public void CreateAbsence_WithACidOnAnAtestado_IsValid()
        {
            var absence = Absence();
            absence.Type = (int)EnumEmployeeAbsenceType.Atestado;
            absence.Cid = "J11";

            Assert.True(new CreateEmployeeAbsenceRequestValidator().Validate(absence).IsValid);
        }

        [Fact]
        public void CreateAbsence_WithACidOnAFalta_IsRefused()
        {
            var absence = Absence();
            absence.Type = (int)EnumEmployeeAbsenceType.Falta;
            absence.Cid = "J11";

            var result = new CreateEmployeeAbsenceRequestValidator().Validate(absence);

            Assert.False(result.IsValid);
            Assert.Contains(result.Errors, e => e.ErrorMessage.Contains("only be recorded on an atestado"));
        }

        [Fact]
        public void UpdateAbsence_CarriesTheSameCidRule()
        {
            var result = new UpdateEmployeeAbsenceRequestValidator().Validate(new UpdateEmployeeAbsenceRequestDTO
            {
                Id = Guid.NewGuid(),
                Type = (int)EnumEmployeeAbsenceType.Falta,
                StartDate = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc),
                EndDate = new DateTime(2026, 9, 2, 0, 0, 0, DateTimeKind.Utc),
                Cid = "J11"
            });

            Assert.False(result.IsValid);
            Assert.Contains(result.Errors, e => e.ErrorMessage.Contains("only be recorded on an atestado"));
        }

        [Fact]
        public void UpdateAbsence_WithoutAnId_IsRefused()
        {
            var result = new UpdateEmployeeAbsenceRequestValidator().Validate(new UpdateEmployeeAbsenceRequestDTO
            {
                Id = Guid.Empty,
                Type = (int)EnumEmployeeAbsenceType.Falta,
                StartDate = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc),
                EndDate = new DateTime(2026, 9, 2, 0, 0, 0, DateTimeKind.Utc)
            });

            Assert.False(result.IsValid);
        }
    }

    public class VacationRequestValidatorTests
    {
        /// <summary>CLT art. 134 §1: no stretch shorter than five days.</summary>
        [Theory]
        [InlineData(5)]
        [InlineData(30)]
        public void ScheduleVacation_InsideTheCltBounds_IsValid(int days)
        {
            var result = new ScheduleVacationRequestValidator().Validate(new ScheduleVacationRequestDTO
            {
                AccrualId = Guid.NewGuid(),
                StartDate = new DateTime(2026, 12, 1, 0, 0, 0, DateTimeKind.Utc),
                Days = days
            });

            Assert.True(result.IsValid);
        }

        [Theory]
        [InlineData(4)]
        [InlineData(31)]
        public void ScheduleVacation_OutsideTheCltBounds_IsRefused(int days)
        {
            var result = new ScheduleVacationRequestValidator().Validate(new ScheduleVacationRequestDTO
            {
                AccrualId = Guid.NewGuid(),
                StartDate = new DateTime(2026, 12, 1, 0, 0, 0, DateTimeKind.Utc),
                Days = days
            });

            Assert.False(result.IsValid);
        }

        [Fact]
        public void ScheduleVacation_WithoutAnAccrual_IsRefused()
        {
            var result = new ScheduleVacationRequestValidator().Validate(new ScheduleVacationRequestDTO
            {
                AccrualId = Guid.Empty,
                StartDate = new DateTime(2026, 12, 1, 0, 0, 0, DateTimeKind.Utc),
                Days = 10
            });

            Assert.False(result.IsValid);
            Assert.Contains(result.Errors, e => e.ErrorMessage.Contains("período aquisitivo"));
        }

        /// <summary>
        /// CLT art. 143 caps the abono at a third of the entitlement. A third of the largest
        /// possible entitlement is ten, so ten holds here without loading the accrual.
        /// </summary>
        [Theory]
        [InlineData(1)]
        [InlineData(10)]
        public void SellVacationDays_UpToTheArt143Ceiling_IsValid(int days)
        {
            var result = new SellVacationDaysRequestValidator().Validate(new SellVacationDaysRequestDTO
            {
                AccrualId = Guid.NewGuid(),
                Days = days
            });

            Assert.True(result.IsValid);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(11)]
        public void SellVacationDays_OutsideTheArt143Ceiling_IsRefused(int days)
        {
            var result = new SellVacationDaysRequestValidator().Validate(new SellVacationDaysRequestDTO
            {
                AccrualId = Guid.NewGuid(),
                Days = days
            });

            Assert.False(result.IsValid);
        }
    }

    public class UpdatePayrollSettingsRequestValidatorTests
    {
        private static EnterprisePayrollSettingsDTO Settings() => new()
        {
            InssRate = 0.20m,
            RatRate = 0.02m,
            ThirdPartyRate = 0.058m,
            FgtsRate = 0.08m,
            PayrollDueDay = 5
        };

        [Fact]
        public void Validate_WithTheUsualBrazilianRates_IsValid()
        {
            Assert.True(new UpdatePayrollSettingsRequestValidator().Validate(Settings()).IsValid);
        }

        /// <summary>
        /// The rates are fractions, not percentages — 20% is 0.20. Sending 20 is the paste error
        /// that would multiply an entire folha by a hundred.
        /// </summary>
        [Fact]
        public void Validate_WithARateSentAsAPercentage_IsRefused()
        {
            var settings = Settings();
            settings.InssRate = 20m;

            Assert.False(new UpdatePayrollSettingsRequestValidator().Validate(settings).IsValid);
        }

        [Fact]
        public void Validate_WithANegativeRate_IsRefused()
        {
            var settings = Settings();
            settings.FgtsRate = -0.01m;

            Assert.False(new UpdatePayrollSettingsRequestValidator().Validate(settings).IsValid);
        }

        /// <summary>
        /// The 28th is the ceiling because every month has one; a 30 would skip February.
        /// </summary>
        [Theory]
        [InlineData(0)]
        [InlineData(29)]
        [InlineData(31)]
        public void Validate_WithAPayrollDayNotEveryMonthHas_IsRefused(int day)
        {
            var settings = Settings();
            settings.PayrollDueDay = day;

            Assert.False(new UpdatePayrollSettingsRequestValidator().Validate(settings).IsValid);
        }
    }
}
