using EvangelionERPV2.StorefrontModule.Application.DTOs;
using EvangelionERPV2.StorefrontModule.Application.Services;
using FluentValidation;

namespace EvangelionERPV2.Web.FluentValidator
{
    public sealed class StorefrontLoginRequestValidator : AbstractValidator<StorefrontLoginRequestDTO>
    {
        public StorefrontLoginRequestValidator()
        {
            RuleFor(x => x.UserName)
                .NotEmpty().WithMessage("UserName is required.")
                .MaximumLength(256).WithMessage("UserName is too long.");

            RuleFor(x => x.Password)
                .NotEmpty().WithMessage("Password is required.")
                .MaximumLength(256).WithMessage("Password is too long.");
        }
    }

    /// <summary>
    /// Shape only. The rules a shop owner needs explained — slug format and uniqueness, delivery
    /// ranges, what listing requires — live in <see cref="StorefrontService"/>, in pt-BR.
    /// </summary>
    public sealed class SetStorefrontListingRequestValidator : AbstractValidator<SetStorefrontListingRequestDTO>
    {
        public SetStorefrontListingRequestValidator()
        {
            RuleFor(x => x.Slug).MaximumLength(60).WithMessage("Slug is too long.");
            RuleFor(x => x.Description).MaximumLength(500).WithMessage("Description is too long.");
            RuleFor(x => x.LogoUrl).MaximumLength(500).WithMessage("LogoUrl is too long.");
            RuleFor(x => x.Policies).MaximumLength(4000).WithMessage("Policies is too long.");
        }
    }

    public sealed class SetStorefrontPublishedRequestValidator : AbstractValidator<SetStorefrontPublishedRequestDTO>
    {
        public SetStorefrontPublishedRequestValidator()
        {
            RuleFor(x => x.ProductIds)
                .NotNull().WithMessage("ProductIds is required.")
                .Must(ids => ids.Count <= StorefrontService.MaxProductIdsPerCall)
                .WithMessage($"At most {StorefrontService.MaxProductIdsPerCall} products per call.");

            RuleForEach(x => x.ProductIds)
                .NotEmpty().WithMessage("ProductIds must not contain an empty id.");
        }
    }

    public sealed class StorefrontCartLineValidator : AbstractValidator<StorefrontCartLineDTO>
    {
        public StorefrontCartLineValidator()
        {
            RuleFor(x => x.ProductId).NotEmpty().WithMessage("ProductId is required.");
            RuleFor(x => x.Quantity).InclusiveBetween(1, 999).WithMessage("Quantity must be between 1 and 999.");
        }
    }

    public sealed class StorefrontQuoteRequestValidator : AbstractValidator<StorefrontQuoteRequestDTO>
    {
        public StorefrontQuoteRequestValidator()
        {
            RuleFor(x => x.Lines).NotEmpty().Must(lines => lines.Count <= 100).WithMessage("Between 1 and 100 lines.");
            RuleForEach(x => x.Lines).NotNull().SetValidator(new StorefrontCartLineValidator());
        }
    }

    /// <summary>Shape only: Storefront.Api validates CPF, CEP and the rest in pt-BR before it calls.</summary>
    public sealed class StorefrontCheckoutRequestValidator : AbstractValidator<StorefrontCheckoutRequestDTO>
    {
        public StorefrontCheckoutRequestValidator()
        {
            RuleFor(x => x.Lines).NotEmpty().Must(lines => lines.Count <= 100).WithMessage("Between 1 and 100 lines.");
            RuleForEach(x => x.Lines).NotNull().SetValidator(new StorefrontCartLineValidator());
            RuleFor(x => x.PaymentMethod).IsInEnum();
            RuleFor(x => x.Installments).InclusiveBetween(1, StorefrontInstallments.MaxCount);
            RuleFor(x => x.ExpectedTotal).GreaterThan(0);

            RuleFor(x => x.Customer).NotNull();
            RuleFor(x => x.Customer.Email).NotEmpty().MaximumLength(254).EmailAddress().When(x => x.Customer != null);
            RuleFor(x => x.Customer.FullName).NotEmpty().MaximumLength(120).When(x => x.Customer != null);
            RuleFor(x => x.Customer.Cpf).NotEmpty().Matches("^[0-9]{11}$").When(x => x.Customer != null);
            RuleFor(x => x.Customer.Phone).NotEmpty().MaximumLength(20).When(x => x.Customer != null);

            RuleFor(x => x.Address).NotNull();
            RuleFor(x => x.Address.Cep).NotEmpty().Matches("^[0-9]{8}$").When(x => x.Address != null);
            RuleFor(x => x.Address.Street).NotEmpty().MaximumLength(120).When(x => x.Address != null);
            RuleFor(x => x.Address.Number).MaximumLength(20).When(x => x.Address != null);
            RuleFor(x => x.Address.Complement).MaximumLength(60).When(x => x.Address != null);
            RuleFor(x => x.Address.District).NotEmpty().MaximumLength(60).When(x => x.Address != null);
            RuleFor(x => x.Address.City).NotEmpty().MaximumLength(60).When(x => x.Address != null);
            RuleFor(x => x.Address.Uf).NotEmpty().Matches("^[A-Z]{2}$").When(x => x.Address != null);
            RuleFor(x => x.Address.IbgeCode).Matches("^[0-9]{7}$").When(x => x.Address?.IbgeCode != null);
        }
    }

    public sealed class StorefrontSimulateRequestValidator : AbstractValidator<StorefrontSimulateRequestDTO>
    {
        public StorefrontSimulateRequestValidator() => RuleFor(x => x.Outcome).IsInEnum();
    }

    public sealed class StorefrontSimulateStepRequestValidator : AbstractValidator<StorefrontSimulateStepRequestDTO>
    {
        public StorefrontSimulateStepRequestValidator()
        {
            RuleFor(x => x.CheckoutId).NotEmpty();
            RuleFor(x => x.OrderNumber).NotEmpty().MaximumLength(20);
            RuleFor(x => x.Step).IsInEnum();
        }
    }

    public sealed class StorefrontOrderRefValidator : AbstractValidator<StorefrontOrderRefDTO>
    {
        public StorefrontOrderRefValidator()
        {
            RuleFor(x => x.CheckoutId).NotEmpty();
            RuleFor(x => x.OrderNumber).NotEmpty().MaximumLength(20);
        }
    }

    public sealed class StorefrontAttachRequestValidator : AbstractValidator<StorefrontAttachRequestDTO>
    {
        public StorefrontAttachRequestValidator() => RuleFor(x => x.ShopperId).NotEmpty();
    }

    public sealed class StorefrontAttachByEmailRequestValidator : AbstractValidator<StorefrontAttachByEmailRequestDTO>
    {
        public StorefrontAttachByEmailRequestValidator()
        {
            RuleFor(x => x.ShopperId).NotEmpty();
            RuleFor(x => x.Email).NotEmpty().MaximumLength(254);
        }
    }

    /// <summary>Shape only; the tree rules and their pt-BR messages live in <see cref="StorefrontCategoryService"/>.</summary>
    public sealed class SaveStorefrontCategoryRequestValidator : AbstractValidator<SaveStorefrontCategoryRequestDTO>
    {
        public SaveStorefrontCategoryRequestValidator()
        {
            RuleFor(x => x.Name).NotNull().MaximumLength(60);
            RuleFor(x => x.Slug).NotNull().MaximumLength(60);
            RuleFor(x => x.Icon).NotNull().MaximumLength(40);
            RuleFor(x => x.Description).MaximumLength(160);
            RuleFor(x => x.SortOrder).InclusiveBetween(0, 10_000);
        }
    }

    /// <summary>Shape only; P1–P7 and their pt-BR messages live in <see cref="StorefrontPromotionService"/>.</summary>
    public sealed class SetStorefrontPromotionRequestValidator : AbstractValidator<SetStorefrontPromotionRequestDTO>
    {
        public SetStorefrontPromotionRequestValidator()
        {
            RuleFor(x => x.ProductId).NotEmpty();
            RuleFor(x => x.CompareAtPrice).GreaterThan(0).LessThan(10_000_000);
            RuleFor(x => x.EndsAt).NotEmpty();
        }
    }

    public sealed class MoveStorefrontCategoryRequestValidator : AbstractValidator<MoveStorefrontCategoryRequestDTO>
    {
        public MoveStorefrontCategoryRequestValidator() => RuleFor(x => x.SortOrder).InclusiveBetween(0, 10_000);
    }

    public sealed class SetProductCategoryRequestValidator : AbstractValidator<SetProductCategoryRequestDTO>
    {
        public SetProductCategoryRequestValidator()
        {
            RuleFor(x => x.CategoryId).NotEmpty();
            RuleFor(x => x.ProductIds)
                .NotNull()
                .Must(ids => ids.Count <= StorefrontCategoryService.MaxProductsPerAssignment)
                .WithMessage($"At most {StorefrontCategoryService.MaxProductsPerAssignment} products per call.");
            RuleForEach(x => x.ProductIds).NotEmpty();
        }
    }

    public sealed class UploadStorefrontImageRequestValidator : AbstractValidator<UploadStorefrontImageRequestDTO>
    {
        public UploadStorefrontImageRequestValidator()
        {
            RuleFor(x => x.Kind)
                .NotEmpty().WithMessage("kind is required.")
                .Must(k => k is not null && (k.Equals("logo", StringComparison.OrdinalIgnoreCase) || k.Equals("cover", StringComparison.OrdinalIgnoreCase)))
                .WithMessage("kind must be logo or cover.");

            RuleFor(x => x.File).NotEmpty().WithMessage("file is required.");
        }
    }
}
