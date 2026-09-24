using EvangelionERPV2.Shared.DTOs;
using EvangelionERPV2.Shared.Enums;
using EvangelionERPV2.Web.Controllers;
using EvangelionERPV2.Web.FluentValidator;

namespace EvangelionERPV2.Test.Security
{
    /// <summary>
    /// The authentication and self-service bodies. Everything here is reachable before a token
    /// exists, which is what makes the length bounds part of the defence rather than tidiness.
    /// </summary>
    public class AccountRequestValidatorTests
    {
        [Fact]
        public void Login_WithAUserAndAPassword_IsValid()
        {
            var result = new LoginRequestValidator().Validate(new LoginRequestDTO
            {
                UserName = "aureo",
                Password = "s3nh4-forte"
            });

            Assert.True(result.IsValid);
        }

        [Fact]
        public void Login_WithoutCredentials_IsRefused()
        {
            var result = new LoginRequestValidator().Validate(new LoginRequestDTO());

            Assert.False(result.IsValid);
            Assert.Equal(2, result.Errors.Count);
        }

        [Fact]
        public void Login_WithOversizedFields_IsRefused()
        {
            var result = new LoginRequestValidator().Validate(new LoginRequestDTO
            {
                UserName = new string('u', 101),
                Password = new string('p', 129),
                RecaptchaToken = new string('t', 4097)
            });

            Assert.False(result.IsValid);
            Assert.Equal(3, result.Errors.Count);
        }

        [Fact]
        public void GoogleCodeExchange_WithAWholePkceExchange_IsValid()
        {
            var result = new GoogleCodeExchangeRequestValidator().Validate(new GoogleCodeExchangeRequest
            {
                Code = "4/0Adeu5",
                RedirectUri = "https://app.example.com/oauth/callback",
                CodeVerifier = new string('v', 43)
            });

            Assert.True(result.IsValid);
        }

        /// <summary>
        /// "/oauth/callback" is the one that matters: on Linux <c>Uri.TryCreate</c> accepts a
        /// rooted path as an absolute file uri, so <c>UriKind.Absolute</c> on its own let it
        /// through on the platform the API actually runs on.
        /// </summary>
        [Theory]
        [InlineData("/oauth/callback")]
        [InlineData("not a uri")]
        [InlineData("file:///etc/passwd")]
        [InlineData("javascript:alert(1)")]
        public void GoogleCodeExchange_WithARedirectThatIsNotAnHttpUri_IsRefused(string redirectUri)
        {
            var result = new GoogleCodeExchangeRequestValidator().Validate(new GoogleCodeExchangeRequest
            {
                Code = "4/0Adeu5",
                RedirectUri = redirectUri,
                CodeVerifier = new string('v', 43)
            });

            Assert.False(result.IsValid);
            Assert.Contains(result.Errors, e => e.ErrorMessage.Contains("http or https"));
        }

        [Theory]
        [InlineData("http://localhost:8082/oauth/callback")]
        [InlineData("https://app.example.com/oauth/callback")]
        public void GoogleCodeExchange_WithAnHttpRedirect_IsValid(string redirectUri)
        {
            var result = new GoogleCodeExchangeRequestValidator().Validate(new GoogleCodeExchangeRequest
            {
                Code = "4/0Adeu5",
                RedirectUri = redirectUri,
                CodeVerifier = new string('v', 43)
            });

            Assert.True(result.IsValid);
        }

        /// <summary>RFC 7636 fixes the verifier at 43–128 characters; outside that it is not PKCE.</summary>
        [Theory]
        [InlineData(42)]
        [InlineData(129)]
        public void GoogleCodeExchange_WithAVerifierOutsideThePkceLength_IsRefused(int length)
        {
            var result = new GoogleCodeExchangeRequestValidator().Validate(new GoogleCodeExchangeRequest
            {
                Code = "4/0Adeu5",
                RedirectUri = "https://app.example.com/oauth/callback",
                CodeVerifier = new string('v', length)
            });

            Assert.False(result.IsValid);
            Assert.Contains(result.Errors, e => e.ErrorMessage.Contains("43 and 128"));
        }

        [Fact]
        public void GoogleCodeExchange_WithoutACode_IsRefused()
        {
            var result = new GoogleCodeExchangeRequestValidator().Validate(new GoogleCodeExchangeRequest
            {
                Code = string.Empty,
                RedirectUri = "https://app.example.com/oauth/callback",
                CodeVerifier = new string('v', 43)
            });

            Assert.False(result.IsValid);
        }

        [Fact]
        public void RequestPasswordReset_WithAnEmail_IsValid()
        {
            var result = new RequestPasswordResetRequestValidator().Validate(
                new UserController.RequestPasswordResetRequest { Email = "aureo@example.com" });

            Assert.True(result.IsValid);
        }

        [Theory]
        [InlineData("")]
        [InlineData("nope")]
        public void RequestPasswordReset_WithoutAUsableEmail_IsRefused(string email)
        {
            var result = new RequestPasswordResetRequestValidator().Validate(
                new UserController.RequestPasswordResetRequest { Email = email });

            Assert.False(result.IsValid);
        }

        [Fact]
        public void ResetPassword_WithATokenAndANewPassword_IsValid()
        {
            var result = new ResetPasswordRequestValidator().Validate(new UserController.ResetPasswordRequest
            {
                Email = "aureo@example.com",
                Token = "HMAC256:abc",
                NewPassword = "s3nh4-nova"
            });

            Assert.True(result.IsValid);
        }

        [Fact]
        public void ResetPassword_WithAShortNewPassword_IsRefused()
        {
            var result = new ResetPasswordRequestValidator().Validate(new UserController.ResetPasswordRequest
            {
                Email = "aureo@example.com",
                Token = "HMAC256:abc",
                NewPassword = "curta"
            });

            Assert.False(result.IsValid);
            Assert.Contains(result.Errors, e => e.ErrorMessage.Contains("at least 8"));
        }

        [Fact]
        public void ResetPassword_WithoutAToken_IsRefused()
        {
            var result = new ResetPasswordRequestValidator().Validate(new UserController.ResetPasswordRequest
            {
                Email = "aureo@example.com",
                Token = string.Empty,
                NewPassword = "s3nh4-nova"
            });

            Assert.False(result.IsValid);
        }

        [Theory]
        [InlineData((short)0)]
        [InlineData((short)10)]
        public void UpdateTheme_InsideTheSupportedRange_IsValid(short theme)
        {
            Assert.True(new UpdateThemeRequestValidator()
                .Validate(new UserController.UpdateThemeRequest { Theme = theme }).IsValid);
        }

        [Theory]
        [InlineData((short)-1)]
        [InlineData((short)11)]
        public void UpdateTheme_OutsideTheSupportedRange_IsRefused(short theme)
        {
            Assert.False(new UpdateThemeRequestValidator()
                .Validate(new UserController.UpdateThemeRequest { Theme = theme }).IsValid);
        }

        [Fact]
        public void UpdateLanguage_WithASupportedLanguage_IsValid()
        {
            Assert.True(new UpdateLanguageRequestValidator()
                .Validate(new UserController.UpdateLanguageRequest { Language = (int)EnumLanguage.Portuguese })
                .IsValid);
        }

        [Fact]
        public void UpdateLanguage_WithAnUnknownLanguage_IsRefused()
        {
            Assert.False(new UpdateLanguageRequestValidator()
                .Validate(new UserController.UpdateLanguageRequest { Language = 99 }).IsValid);
        }

        /// <summary>Clearing the picture is a legitimate update, so the image rules must not fire.</summary>
        [Theory]
        [InlineData(null)]
        [InlineData("")]
        public void UpdateProfilePicture_WithNothing_IsValid(string? picture)
        {
            Assert.True(new UpdateProfilePictureRequestValidator()
                .Validate(new UserController.UpdateProfilePictureRequest { ProfilePicture = picture }).IsValid);
        }

        [Fact]
        public void UpdateProfilePicture_WithSomethingThatIsNotAnImage_IsRefused()
        {
            var result = new UpdateProfilePictureRequestValidator().Validate(
                new UserController.UpdateProfilePictureRequest
                {
                    ProfilePicture = Convert.ToBase64String("plain text"u8.ToArray())
                });

            Assert.False(result.IsValid);
        }

        [Fact]
        public void UpdateProfile_WithBothNames_IsValid()
        {
            var result = new UpdateProfileRequestValidator().Validate(new UserController.UpdateProfileRequest
            {
                FirstName = "Aureo",
                LastName = "Junior",
                PhoneNumber = "+55 51 99999-0000"
            });

            Assert.True(result.IsValid);
        }

        /// <summary>A null phone means "not given"; only a present one is length-checked.</summary>
        [Fact]
        public void UpdateProfile_WithNoPhone_IsValid()
        {
            var result = new UpdateProfileRequestValidator().Validate(new UserController.UpdateProfileRequest
            {
                FirstName = "Aureo",
                LastName = "Junior",
                PhoneNumber = null
            });

            Assert.True(result.IsValid);
        }

        [Fact]
        public void UpdateProfile_WithAnOversizedPhone_IsRefused()
        {
            var result = new UpdateProfileRequestValidator().Validate(new UserController.UpdateProfileRequest
            {
                FirstName = "Aureo",
                LastName = "Junior",
                PhoneNumber = new string('9', 31)
            });

            Assert.False(result.IsValid);
        }

        [Fact]
        public void UpdateProfile_WithoutANameAtAll_IsRefused()
        {
            var result = new UpdateProfileRequestValidator().Validate(new UserController.UpdateProfileRequest());

            Assert.False(result.IsValid);
            Assert.Equal(2, result.Errors.Count);
        }
    }

    /// <summary>
    /// Bodies whose numbers drive query cost. Unbounded, one request asks for a decade of history
    /// or a page of a million rows, which is why these bounds are assertions and not defaults.
    /// </summary>
    public class AnalyticsRequestValidatorTests
    {
        private static RunSimulationRequestDTO Simulation() => new()
        {
            HorizonInDays = 90,
            Scenarios = [new ForecastSimulationScenarioDTO { ScenarioName = "Base" }]
        };

        [Fact]
        public void RunSimulation_WithOneScenarioInsideTheHorizon_IsValid()
        {
            Assert.True(new RunSimulationRequestValidator().Validate(Simulation()).IsValid);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(366)]
        public void RunSimulation_OutsideTheHorizon_IsRefused(int days)
        {
            var request = Simulation();
            request.HorizonInDays = days;

            Assert.False(new RunSimulationRequestValidator().Validate(request).IsValid);
        }

        [Fact]
        public void RunSimulation_WithNoScenarios_IsRefused()
        {
            var request = Simulation();
            request.Scenarios = [];

            Assert.False(new RunSimulationRequestValidator().Validate(request).IsValid);
        }

        [Fact]
        public void RunSimulation_WithMoreThanTwentyScenarios_IsRefused()
        {
            var request = Simulation();
            request.Scenarios = Enumerable.Range(0, 21)
                .Select(i => new ForecastSimulationScenarioDTO { ScenarioName = $"S{i}" })
                .ToList();

            var result = new RunSimulationRequestValidator().Validate(request);

            Assert.False(result.IsValid);
            Assert.Contains(result.Errors, e => e.ErrorMessage.Contains("At most 20"));
        }

        [Fact]
        public void RunSimulation_WithAnUnnamedScenario_IsRefused()
        {
            var request = Simulation();
            request.Scenarios = [new ForecastSimulationScenarioDTO { ScenarioName = string.Empty }];

            Assert.False(new RunSimulationRequestValidator().Validate(request).IsValid);
        }

        [Theory]
        [InlineData(-366)]
        [InlineData(366)]
        public void RunSimulation_WithAReceivableDelayOutOfRange_IsRefused(int delay)
        {
            var request = Simulation();
            request.Scenarios =
                [new ForecastSimulationScenarioDTO { ScenarioName = "Base", ReceivableDelayInDays = delay }];

            Assert.False(new RunSimulationRequestValidator().Validate(request).IsValid);
        }

        [Theory]
        [InlineData(-0.01)]
        [InlineData(100.01)]
        public void RunSimulation_WithAPayableMultiplierOutOfRange_IsRefused(decimal multiplier)
        {
            var request = Simulation();
            request.Scenarios =
                [new ForecastSimulationScenarioDTO { ScenarioName = "Base", PayableMultiplier = multiplier }];

            Assert.False(new RunSimulationRequestValidator().Validate(request).IsValid);
        }

        [Theory]
        [InlineData("accepted")]
        [InlineData("REJECTED")]
        [InlineData("Ignored")]
        [InlineData("done")]
        public void OpportunityFeedback_WithAKnownStatusInAnyCasing_IsValid(string status)
        {
            Assert.True(new OpportunityFeedbackRequestValidator()
                .Validate(new OpportunityFeedbackRequestDTO { Status = status }).IsValid);
        }

        [Theory]
        [InlineData("")]
        [InlineData("maybe")]
        public void OpportunityFeedback_WithAnUnknownStatus_IsRefused(string status)
        {
            Assert.False(new OpportunityFeedbackRequestValidator()
                .Validate(new OpportunityFeedbackRequestDTO { Status = status }).IsValid);
        }

        [Fact]
        public void OpportunityFeedback_WithAnOversizedCommentOrMetadata_IsRefused()
        {
            var result = new OpportunityFeedbackRequestValidator().Validate(new OpportunityFeedbackRequestDTO
            {
                Status = "done",
                Comment = new string('c', 2001),
                MetadataJson = new string('m', 4001)
            });

            Assert.False(result.IsValid);
            Assert.Equal(2, result.Errors.Count);
        }

        [Theory]
        [InlineData(1)]
        [InlineData(1095)]
        public void OpportunityRecompute_InsideThreeYears_IsValid(int days)
        {
            Assert.True(new OpportunityRecomputeRequestValidator()
                .Validate(new OpportunityRecomputeRequestDTO { HistoryWindowDays = days }).IsValid);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(1096)]
        public void OpportunityRecompute_OutsideThreeYears_IsRefused(int days)
        {
            Assert.False(new OpportunityRecomputeRequestValidator()
                .Validate(new OpportunityRecomputeRequestDTO { HistoryWindowDays = days }).IsValid);
        }

        [Fact]
        public void ReplenishmentSuggestion_WithItsDefaults_IsValid()
        {
            Assert.True(new ReplenishmentSuggestionRequestValidator()
                .Validate(new ReplenishmentSuggestionRequestDTO()).IsValid);
        }

        [Theory]
        [InlineData(0, 50, 180)]
        [InlineData(1, 0, 180)]
        [InlineData(1, 201, 180)]
        [InlineData(1, 50, 0)]
        [InlineData(1, 50, 1096)]
        public void ReplenishmentSuggestion_OutsideAnyBound_IsRefused(int page, int size, int window)
        {
            var result = new ReplenishmentSuggestionRequestValidator().Validate(
                new ReplenishmentSuggestionRequestDTO
                {
                    PageNumber = page,
                    PageSize = size,
                    HistoryWindowDays = window
                });

            Assert.False(result.IsValid);
        }

        [Fact]
        public void AuditTrailFilter_WithAnEmptyFilter_IsValid()
        {
            Assert.True(new AuditTrailFilterValidator().Validate(new AuditTrailFilterDTO()).IsValid);
        }

        [Fact]
        public void AuditTrailFilter_WithAPeriodThatEndsBeforeItStarts_IsRefused()
        {
            var result = new AuditTrailFilterValidator().Validate(new AuditTrailFilterDTO
            {
                ChangedFrom = new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc),
                ChangedTo = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)
            });

            Assert.False(result.IsValid);
            Assert.Contains(result.Errors, e => e.ErrorMessage.Contains("must not precede"));
        }

        /// <summary>One end of an open-ended period is not a period with a bad order.</summary>
        [Fact]
        public void AuditTrailFilter_WithOnlyAStart_IsValid()
        {
            var result = new AuditTrailFilterValidator().Validate(new AuditTrailFilterDTO
            {
                ChangedFrom = new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc)
            });

            Assert.True(result.IsValid);
        }

        [Fact]
        public void AuditTrailFilter_WithOversizedTextFilters_IsRefused()
        {
            var result = new AuditTrailFilterValidator().Validate(new AuditTrailFilterDTO
            {
                UserName = new string('u', 101),
                EntityName = new string('e', 101),
                Action = new string('a', 51)
            });

            Assert.False(result.IsValid);
            Assert.Equal(3, result.Errors.Count);
        }
    }
}
