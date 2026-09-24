using EvangelionERPV2.Shared.DTOs;
using EvangelionERPV2.Shared.Entities;
using EvangelionERPV2.Shared.Enums;
using EvangelionERPV2.Web.FluentValidator;

namespace EvangelionERPV2.Test.Security
{
    public class UserRequestValidatorTests
    {
        /// <summary>1×1 transparent PNG, small enough to keep the assertion about the rule.</summary>
        private const string PngPixel =
            "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==";

        private static CreateUserRequestDTO NewUser() => new()
        {
            FirstName = "Aureo",
            LastName = "Junior",
            UserName = "aureo",
            Password = "s3nh4-forte",
            Email = "aureo@example.com",
            BirthDate = new DateTime(1990, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            Language = (short)EnumLanguage.Portuguese
        };

        private static UpdateUserRequestDTO EditUser() => new()
        {
            Id = Guid.NewGuid(),
            FirstName = "Aureo",
            LastName = "Junior",
            UserName = "aureo",
            Email = "aureo@example.com",
            BirthDate = new DateTime(1990, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            AccessLevel = (short)EnumAccessLevel.Manager,
            Language = (short)EnumLanguage.Portuguese
        };

        [Fact]
        public void CreateUser_WithEveryRequiredField_IsValid()
        {
            Assert.True(new CreateUserRequestValidator().Validate(NewUser()).IsValid);
        }

        [Theory]
        [InlineData("")]
        [InlineData("not-an-email")]
        [InlineData("missing@")]
        public void CreateUser_WithoutAUsableEmail_IsRefused(string email)
        {
            var request = NewUser();
            request.Email = email;

            Assert.False(new CreateUserRequestValidator().Validate(request).IsValid);
        }

        [Theory]
        [InlineData("")]
        [InlineData("short7c")]
        public void CreateUser_WithAPasswordUnderEightCharacters_IsRefused(string password)
        {
            var request = NewUser();
            request.Password = password;

            Assert.False(new CreateUserRequestValidator().Validate(request).IsValid);
        }

        [Fact]
        public void CreateUser_WithAPasswordOver128Characters_IsRefused()
        {
            var request = NewUser();
            request.Password = new string('x', 129);

            Assert.False(new CreateUserRequestValidator().Validate(request).IsValid);
        }

        [Fact]
        public void CreateUser_WithABirthDateInTheFuture_IsRefused()
        {
            var request = NewUser();
            request.BirthDate = DateTime.UtcNow.AddDays(1);

            var result = new CreateUserRequestValidator().Validate(request);

            Assert.False(result.IsValid);
            Assert.Contains(result.Errors, e => e.ErrorMessage.Contains("in the past"));
        }

        [Fact]
        public void CreateUser_WithALanguageOutsideTheEnum_IsRefused()
        {
            var request = NewUser();
            request.Language = 99;

            Assert.False(new CreateUserRequestValidator().Validate(request).IsValid);
        }

        [Theory]
        [InlineData("Aureo", "")]
        [InlineData("", "Junior")]
        public void CreateUser_WithoutBothNames_IsRefused(string first, string last)
        {
            var request = NewUser();
            request.FirstName = first;
            request.LastName = last;

            Assert.False(new CreateUserRequestValidator().Validate(request).IsValid);
        }

        [Fact]
        public void CreateUser_WithAPngProfilePicture_IsValid()
        {
            var request = NewUser();
            request.ProfilePicture = PngPixel;

            Assert.True(new CreateUserRequestValidator().Validate(request).IsValid);
        }

        [Fact]
        public void CreateUser_WithAProfilePictureThatIsNotBase64_IsRefused()
        {
            var request = NewUser();
            request.ProfilePicture = "not base64 at all!!";

            var result = new CreateUserRequestValidator().Validate(request);

            Assert.False(result.IsValid);
            Assert.Contains(result.Errors, e => e.ErrorMessage.Contains("base64", StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Valid base64 of something that is not an image. The signature check is what stops an
        /// arbitrary payload being stored as a profile picture.
        /// </summary>
        [Fact]
        public void CreateUser_WithABase64PayloadThatIsNotAnImage_IsRefused()
        {
            var request = NewUser();
            request.ProfilePicture = Convert.ToBase64String("<?php echo 1; ?>"u8.ToArray());

            var result = new CreateUserRequestValidator().Validate(request);

            Assert.False(result.IsValid);
            Assert.Contains(result.Errors, e => e.ErrorMessage.Contains("PNG"));
        }

        [Fact]
        public void UpdateUser_WithoutAnId_IsRefused()
        {
            var request = EditUser();
            request.Id = Guid.Empty;

            Assert.False(new UpdateUserRequestValidator().Validate(request).IsValid);
        }

        /// <summary>
        /// A blank password on an update means "leave it alone", so the length rules must not fire.
        /// </summary>
        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public void UpdateUser_WithNoNewPassword_IsValid(string? password)
        {
            var request = EditUser();
            request.Password = password;

            Assert.True(new UpdateUserRequestValidator().Validate(request).IsValid);
        }

        [Fact]
        public void UpdateUser_WithAShortNewPassword_IsRefused()
        {
            var request = EditUser();
            request.Password = "short";

            Assert.False(new UpdateUserRequestValidator().Validate(request).IsValid);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(0.035)]
        [InlineData(1)]
        public void UpdateUser_WithACommissionRateInRange_IsValid(decimal rate)
        {
            var request = EditUser();
            request.CommissionRate = rate;

            Assert.True(new UpdateUserRequestValidator().Validate(request).IsValid);
        }

        [Theory]
        [InlineData(-0.01)]
        [InlineData(1.01)]
        public void UpdateUser_WithACommissionRateOutsideZeroToOne_IsRefused(decimal rate)
        {
            var request = EditUser();
            request.CommissionRate = rate;

            var result = new UpdateUserRequestValidator().Validate(request);

            Assert.False(result.IsValid);
            Assert.Contains(result.Errors, e => e.ErrorMessage.Contains("Commission rate"));
        }

        /// <summary>Null is "keep what is stored", which the rule must not treat as out of range.</summary>
        [Fact]
        public void UpdateUser_WithNoCommissionRate_IsValid()
        {
            var request = EditUser();
            request.CommissionRate = null;

            Assert.True(new UpdateUserRequestValidator().Validate(request).IsValid);
        }

        [Fact]
        public void UpdateUser_WithAnAccessLevelOutsideTheEnum_IsRefused()
        {
            var request = EditUser();
            request.AccessLevel = 42;

            Assert.False(new UpdateUserRequestValidator().Validate(request).IsValid);
        }
    }

    /// <summary>
    /// The document check is the only real logic in the customer validator, and a wrong answer
    /// either blocks a legitimate customer or lets an unusable CPF onto a nota.
    /// </summary>
    public class CustomerValidatorTests
    {
        private static Customer NewCustomer(string? document = null) => new()
        {
            Name = "Cliente",
            EnterpriseId = Guid.NewGuid(),
            Document = document
        };

        [Fact]
        public void Validate_WithANameAndAnEnterprise_IsValid()
        {
            Assert.True(new CustomerValidator().Validate(NewCustomer()).IsValid);
        }

        [Theory]
        [InlineData("")]
        [InlineData(null)]
        public void Validate_WithoutAName_IsRefused(string? name)
        {
            var customer = NewCustomer();
            customer.Name = name!;

            var result = new CustomerValidator().Validate(customer);

            Assert.False(result.IsValid);
            Assert.Contains(result.Errors, e => e.ErrorMessage.Contains("must have a Name"));
        }

        [Fact]
        public void Validate_WithoutAnEnterprise_IsRefused()
        {
            var customer = NewCustomer();
            customer.EnterpriseId = Guid.Empty;

            var result = new CustomerValidator().Validate(customer);

            Assert.False(result.IsValid);
            Assert.Contains(result.Errors, e => e.ErrorMessage.Contains("must have a Enterprise"));
        }

        /// <summary>A customer with no document at all is legitimate — a walk-in.</summary>
        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public void Validate_WithNoDocument_SkipsTheDocumentRule(string? document)
        {
            Assert.True(new CustomerValidator().Validate(NewCustomer(document)).IsValid);
        }

        [Theory]
        [InlineData("52998224725")]
        [InlineData("529.982.247-25")]
        [InlineData("11144477735")]
        public void Validate_WithAValidCpf_IsValid(string document)
        {
            Assert.True(new CustomerValidator().Validate(NewCustomer(document)).IsValid);
        }

        [Theory]
        [InlineData("11222333000181")]
        [InlineData("11.222.333/0001-81")]
        public void Validate_WithAValidCnpj_IsValid(string document)
        {
            Assert.True(new CustomerValidator().Validate(NewCustomer(document)).IsValid);
        }

        [Fact]
        public void Validate_WithAValidPortugueseNif_IsValid()
        {
            Assert.True(new CustomerValidator().Validate(NewCustomer("501442600")).IsValid);
        }

        [Theory]
        [InlineData("52998224724")]          // CPF, last check digit wrong
        [InlineData("11222333000182")]       // CNPJ, last check digit wrong
        [InlineData("501442601")]            // NIF, check digit wrong
        [InlineData("123")]                  // too short for anything
        [InlineData("1234567890123456")]     // too long for anything
        public void Validate_WithADocumentThatFailsItsCheckDigits_IsRefused(string document)
        {
            var result = new CustomerValidator().Validate(NewCustomer(document));

            Assert.False(result.IsValid);
            Assert.Contains(result.Errors, e => e.ErrorMessage.Contains("valid CPF, CNPJ, or NIF"));
        }

        /// <summary>
        /// Eleven identical digits pass the CPF check-digit arithmetic and are still not a CPF.
        /// The same holds for fourteen of them against CNPJ.
        /// </summary>
        [Theory]
        [InlineData("00000000000")]
        [InlineData("11111111111")]
        [InlineData("00000000000000")]
        public void Validate_WithARepeatedDigitDocument_IsRefused(string document)
        {
            Assert.False(new CustomerValidator().Validate(NewCustomer(document)).IsValid);
        }

        /// <summary>A NIF's first digit is drawn from a fixed set; nothing else is a NIF.</summary>
        [Theory]
        [InlineData("001442600")]
        [InlineData("401442600")]
        [InlineData("701442600")]
        public void Validate_WithANineDigitNumberThatCannotStartANif_IsRefused(string document)
        {
            Assert.False(new CustomerValidator().Validate(NewCustomer(document)).IsValid);
        }
    }
}
