using EvangelionERPV2.Shared.DTOs;
using FluentValidation;

namespace EvangelionERPV2.Web.FluentValidator
{
    public sealed class ImportSupplierNFeRequestValidator : AbstractValidator<ImportSupplierNFeRequestDTO>
    {
        private const long MaxInboundNFeSizeInBytes = 2 * 1024 * 1024;

        public ImportSupplierNFeRequestValidator()
        {
            RuleFor(x => x.File)
                .NotNull().WithMessage("NF-e file is required.")
                .Must(file => file!.Length > 0).WithMessage("NF-e file is empty.")
                .Must(file => file!.Length <= MaxInboundNFeSizeInBytes)
                    .WithMessage("NF-e file must be 2 MB or smaller.");
        }
    }

    public sealed class ImportBankStatementRequestValidator : AbstractValidator<ImportBankStatementRequestDTO>
    {
        private const long MaxStatementSizeInBytes = 8 * 1024 * 1024;

        public ImportBankStatementRequestValidator()
        {
            RuleFor(x => x.File)
                .NotNull().WithMessage("Statement file is required.")
                .Must(file => file!.Length > 0).WithMessage("Statement file is empty.")
                .Must(file => file!.Length <= MaxStatementSizeInBytes)
                    .WithMessage("Statement file must be 8 MB or smaller.");
        }
    }
}
