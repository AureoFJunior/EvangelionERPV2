using System.Security.Claims;
using EvangelionERPV2.StorefrontModule.Application.DTOs;
using EvangelionERPV2.StorefrontModule.Application.Interface;
using EvangelionERPV2.StorefrontModule.Application.Services;
using EvangelionERPV2.Web.Controllers;
using EvangelionERPV2.Web.FluentValidator;
using FluentValidation;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Moq;

namespace EvangelionERPV2.Test.Security;

public sealed class StorefrontBoundaryTests
{
    private static readonly Guid Platform = Guid.NewGuid();
    private static readonly StorefrontQuoteDTO EmptyQuote = new([], 0, 0, 0, []);

    [Fact]
    public void StorefrontValidators_AcceptValidRequests_AndRejectInvalidShapes()
    {
        var productId = Guid.NewGuid();
        var customer = new StorefrontCustomerDTO("ana@example.com", "Ana Souza", "52998224725", "11987654321", false);
        var address = new StorefrontAddressDTO("01310100", "Avenida Paulista", "1000", null, "Bela Vista", "São Paulo", "SP", "3550308");
        var checkout = new StorefrontCheckoutRequestDTO([new(productId, 1)], customer, address, StorefrontPaymentMethod.Pix, 99, null);

        Assert.True(IsValid(new StorefrontLoginRequestValidator(), new StorefrontLoginRequestDTO { UserName = "limen", Password = "secret" }));
        Assert.False(IsValid(new StorefrontLoginRequestValidator(), new StorefrontLoginRequestDTO()));
        Assert.True(IsValid(new SetStorefrontListingRequestValidator(), new SetStorefrontListingRequestDTO { Slug = "aurora" }));
        Assert.True(IsValid(new SetStorefrontPublishedRequestValidator(), new SetStorefrontPublishedRequestDTO { ProductIds = [productId] }));
        Assert.False(IsValid(new SetStorefrontPublishedRequestValidator(), new SetStorefrontPublishedRequestDTO { ProductIds = [Guid.Empty] }));
        Assert.True(IsValid(new StorefrontCartLineValidator(), new StorefrontCartLineDTO(productId, 1)));
        Assert.False(IsValid(new StorefrontCartLineValidator(), new StorefrontCartLineDTO(Guid.Empty, 0)));
        Assert.True(IsValid(new StorefrontQuoteRequestValidator(), new StorefrontQuoteRequestDTO([new(productId, 1)])));
        Assert.True(IsValid(new StorefrontCheckoutRequestValidator(), checkout));
        Assert.True(IsValid(new StorefrontSimulateRequestValidator(), new StorefrontSimulateRequestDTO(StorefrontSimulatedOutcome.Approve)));
        Assert.True(IsValid(new StorefrontSimulateStepRequestValidator(), new StorefrontSimulateStepRequestDTO(Guid.NewGuid(), "LM-12345678", StorefrontSimulatedOrderStep.Shipped)));
        Assert.True(IsValid(new StorefrontOrderRefValidator(), new StorefrontOrderRefDTO(Guid.NewGuid(), "LM-12345678")));
        Assert.True(IsValid(new StorefrontAttachRequestValidator(), new StorefrontAttachRequestDTO(Guid.NewGuid())));
        Assert.True(IsValid(new StorefrontAttachByEmailRequestValidator(), new StorefrontAttachByEmailRequestDTO("ana@example.com", Guid.NewGuid())));
        Assert.True(IsValid(new SaveStorefrontCategoryRequestValidator(), new SaveStorefrontCategoryRequestDTO { Name = "Casa", Slug = "casa", Icon = "Tag" }));
        Assert.True(IsValid(new SetStorefrontPromotionRequestValidator(), new SetStorefrontPromotionRequestDTO { ProductId = productId, CompareAtPrice = 100, EndsAt = DateTime.UtcNow.AddDays(1) }));
        Assert.True(IsValid(new MoveStorefrontCategoryRequestValidator(), new MoveStorefrontCategoryRequestDTO { SortOrder = 1 }));
        Assert.True(IsValid(new SetProductCategoryRequestValidator(), new SetProductCategoryRequestDTO { CategoryId = Guid.NewGuid(), ProductIds = [productId] }));
        Assert.True(IsValid(new UploadStorefrontImageRequestValidator(), new UploadStorefrontImageRequestDTO { Kind = "logo", File = "data:image/png;base64,AA==" }));
        Assert.False(IsValid(new UploadStorefrontImageRequestValidator(), new UploadStorefrontImageRequestDTO { Kind = "1" }));
    }

    [Fact]
    public async Task CatalogEndpoints_ClampInputs_AndMapMissingResources()
    {
        var storefront = new Mock<IStorefrontService>();
        var categories = new Mock<IStorefrontCategoryService>();
        storefront.Setup(s => s.GetEnterprisesAsync(1, 100)).ReturnsAsync(new StorefrontPageDTO<StorefrontEnterpriseSummaryDTO>([], 0));
        storefront.Setup(s => s.GetEnterpriseAsync("missing")).ReturnsAsync((StorefrontEnterpriseProfileDTO?)null);
        storefront.Setup(s => s.GetProductsAsync(It.IsAny<StorefrontProductQuery>())).ReturnsAsync(new StorefrontPageDTO<StorefrontProductSummaryDTO>([], 0));
        storefront.Setup(s => s.GetProductAsync(It.IsAny<Guid>())).ReturnsAsync((StorefrontProductDetailDTO?)null);
        storefront.Setup(s => s.GetPictureAsync(It.IsAny<Guid>())).ReturnsAsync((StorefrontPictureDTO?)null);
        storefront.Setup(s => s.GetFamilyPictureAsync(It.IsAny<Guid>())).ReturnsAsync(new StorefrontPictureDTO([1], "image/png"));
        storefront.Setup(s => s.GetEnterpriseImageAsync(It.IsAny<string>(), It.IsAny<StorefrontImageKind>())).ReturnsAsync((StorefrontPictureDTO?)null);
        categories.Setup(s => s.GetPublicTreeAsync()).ReturnsAsync([]);
        categories.Setup(s => s.GetPublicPageAsync("missing")).ReturnsAsync((StorefrontCategoryPageDTO?)null);
        var controller = Controller(storefront.Object, Mock.Of<IStorefrontCheckoutService>(), categories.Object);

        Assert.IsType<OkObjectResult>(await controller.Enterprises(0, 500));
        Assert.IsType<NotFoundResult>(await controller.GetEnterprise("missing"));
        Assert.IsType<BadRequestObjectResult>(await controller.Products(1, 20, new string('q', 101), null, null, null));
        Assert.IsType<OkObjectResult>(await controller.Products(0, 500, null, null, null, null));
        Assert.IsType<NotFoundResult>(await controller.GetProduct(Guid.NewGuid()));
        Assert.IsType<NotFoundResult>(await controller.ProductPicture(Guid.NewGuid()));
        Assert.IsType<NotFoundResult>(await controller.EnterpriseLogo("missing"));
        Assert.IsType<NotFoundResult>(await controller.EnterpriseCover("missing"));
        Assert.IsType<FileContentResult>(await controller.FamilyPicture(Guid.NewGuid()));
        Assert.IsType<OkObjectResult>(await controller.Categories());
        Assert.IsType<NotFoundResult>(await controller.CategoryBySlug(new string('x', 61)));
        Assert.IsType<NotFoundResult>(await controller.CategoryBySlug("missing"));
    }

    [Fact]
    public async Task CheckoutEndpoints_MapServiceResults_AndRequireAnActorForWithdrawal()
    {
        var checkout = new Mock<IStorefrontCheckoutService>();
        checkout.SetupGet(s => s.SimulationEnabled).Returns(true);
        checkout.Setup(s => s.QuoteAsync(It.IsAny<IReadOnlyList<StorefrontCartLineDTO>>())).ReturnsAsync(EmptyQuote);
        checkout.Setup(s => s.CreateAsync(It.IsAny<StorefrontCheckoutRequestDTO>())).ReturnsAsync(new StorefrontCheckoutResultDTO(null, EmptyQuote));
        checkout.Setup(s => s.GetAsync(It.IsAny<Guid>())).ReturnsAsync((StorefrontCheckoutDTO?)null);
        checkout.Setup(s => s.RenewPixAsync(It.IsAny<Guid>())).ReturnsAsync((StorefrontCheckoutDTO?)null);
        checkout.Setup(s => s.SimulateAsync(It.IsAny<Guid>(), It.IsAny<StorefrontSimulatedOutcome>())).ReturnsAsync((StorefrontCheckoutDTO?)null);
        checkout.Setup(s => s.ListPurchasesAsync(It.IsAny<Guid>())).ReturnsAsync([]);
        checkout.Setup(s => s.GetPurchaseAsync(It.IsAny<Guid>())).ReturnsAsync((StorefrontPurchaseDTO?)null);
        checkout.Setup(s => s.ListPurchaseContactsAsync(It.IsAny<Guid>())).ReturnsAsync([]);
        checkout.Setup(s => s.OwnerOfAsync(It.IsAny<Guid>())).ReturnsAsync((Guid?)null);
        checkout.Setup(s => s.AttachShopperAsync(It.IsAny<Guid>(), It.IsAny<Guid>())).ReturnsAsync(false);
        checkout.Setup(s => s.AttachGuestPurchasesAsync(It.IsAny<string>(), It.IsAny<Guid>())).ReturnsAsync(0);
        checkout.Setup(s => s.DetachShopperAsync(It.IsAny<Guid>())).Returns(Task.CompletedTask);
        checkout.Setup(s => s.RequestWithdrawalAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<Guid>())).ReturnsAsync((StorefrontOrderDTO?)null);
        checkout.Setup(s => s.SimulateStepAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<StorefrontSimulatedOrderStep>())).ReturnsAsync((StorefrontOrderDTO?)null);
        checkout.Setup(s => s.GetInvoiceAsync(It.IsAny<Guid>(), It.IsAny<string>())).ReturnsAsync((StorefrontInvoiceDTO?)null);
        var controller = Controller(Mock.Of<IStorefrontService>(), checkout.Object, Mock.Of<IStorefrontCategoryService>());
        var id = Guid.NewGuid();
        var request = new StorefrontCheckoutRequestDTO([new(id, 1)], new("a@b.com", "Ana", "52998224725", "11", false), new("1", "R", null, null, "B", "C", "SP"), StorefrontPaymentMethod.Pix, 1, null);

        Assert.IsType<OkObjectResult>(await controller.Quote(new([new(id, 1)])));
        Assert.IsType<ConflictObjectResult>(await controller.CreateCheckout(request));
        Assert.IsType<NotFoundResult>(await controller.GetCheckout(id));
        Assert.IsType<NotFoundResult>(await controller.RenewPix(id));
        Assert.IsType<NotFoundResult>(await controller.Simulate(id, new(StorefrontSimulatedOutcome.Approve)));
        Assert.IsType<OkObjectResult>(await controller.Purchases(id));
        Assert.IsType<NotFoundResult>(await controller.Purchase(id));
        Assert.IsType<OkObjectResult>(await controller.PurchaseContacts(id));
        Assert.IsType<OkObjectResult>(await controller.Owner(id));
        Assert.IsType<ConflictResult>(await controller.AttachShopper(id, new(id)));
        Assert.IsType<OkObjectResult>(await controller.AttachGuestPurchases(new("a@b.com", id)));
        Assert.IsType<NoContentResult>(await controller.DetachShopper(id));
        Assert.IsType<UnauthorizedResult>(await controller.OrderWithdrawal(new(id, "LM-12345678")));
        SetUser(controller, Platform, id);
        Assert.IsType<NotFoundResult>(await controller.OrderWithdrawal(new(id, "LM-12345678")));
        Assert.IsType<NotFoundResult>(await controller.SimulateOrderStep(new(id, "LM-12345678", StorefrontSimulatedOrderStep.Shipped)));
        Assert.IsType<NotFoundResult>(await controller.OrderInvoice(id, "LM-12345678"));
    }

    [Fact]
    public async Task ListingEndpoints_ValidateTenantAndImageKind_AndMapServiceErrors()
    {
        var storefront = new Mock<IStorefrontService>();
        storefront.Setup(s => s.GetListingAsync(Platform)).ReturnsAsync(new StorefrontListingDTO());
        storefront.SetupSequence(s => s.SetListingAsync(Platform, It.IsAny<SetStorefrontListingRequestDTO>()))
            .ThrowsAsync(new ArgumentException("invalid"))
            .ReturnsAsync(new StorefrontListingDTO());
        storefront.SetupSequence(s => s.SetListingImageAsync(Platform, StorefrontImageKind.Logo, It.IsAny<string?>()))
            .ReturnsAsync(new StorefrontListingDTO())
            .ThrowsAsync(new ArgumentException("invalid"));
        storefront.Setup(s => s.GetListingImageAsync(Platform, StorefrontImageKind.Cover)).ReturnsAsync(new StorefrontPictureDTO([1], "image/png"));
        storefront.Setup(s => s.RemoveListingImageAsync(Platform, StorefrontImageKind.Logo)).ReturnsAsync(new StorefrontListingDTO());
        storefront.SetupSequence(s => s.SetPublishedAsync(Platform, It.IsAny<IReadOnlyCollection<Guid>>(), true))
            .ReturnsAsync(1)
            .ThrowsAsync(new ArgumentException("invalid"));
        var controller = Controller(storefront.Object, Mock.Of<IStorefrontCheckoutService>(), Mock.Of<IStorefrontCategoryService>());

        Assert.IsType<UnauthorizedResult>(await controller.Listing());
        SetUser(controller, Platform);
        Assert.IsType<OkObjectResult>(await controller.Listing());
        Assert.IsType<BadRequestObjectResult>(await controller.SetListing(null!));
        Assert.IsType<BadRequestObjectResult>(await controller.SetListing(new()));
        Assert.IsType<OkObjectResult>(await controller.SetListing(new()));
        Assert.IsType<BadRequestObjectResult>(await controller.UploadListingImage(new() { Kind = "1" }));
        Assert.IsType<OkObjectResult>(await controller.UploadListingImage(new() { Kind = "logo", File = "x" }));
        Assert.IsType<BadRequestObjectResult>(await controller.UploadListingImage(new() { Kind = "logo", File = "x" }));
        Assert.IsType<NotFoundResult>(await controller.ListingImage("invalid"));
        Assert.IsType<FileContentResult>(await controller.ListingImage("cover"));
        Assert.IsType<BadRequestObjectResult>(await controller.RemoveListingImage("1"));
        Assert.IsType<OkObjectResult>(await controller.RemoveListingImage("logo"));
        Assert.IsType<BadRequestObjectResult>(await controller.SetPublished(null!));
        Assert.IsType<OkObjectResult>(await controller.SetPublished(new() { ProductIds = [Guid.NewGuid(), Guid.NewGuid()], Published = true }));
        Assert.IsType<BadRequestObjectResult>(await controller.SetPublished(new() { ProductIds = [Guid.NewGuid()], Published = true }));
    }

    [Fact]
    public async Task CategoryEndpoints_EnforcePlatformOwnership_AndMapDomainResults()
    {
        var categories = new Mock<IStorefrontCategoryService>();
        var node = new StorefrontCategoryNodeDTO(Guid.NewGuid(), null, "Casa", "casa", "Tag", null, 0, true, 0, 0, []);
        categories.Setup(s => s.GetTreeAsync(Platform, true)).ReturnsAsync([node]);
        categories.Setup(s => s.UpdateAsync(It.IsAny<Guid>(), It.IsAny<SaveStorefrontCategoryRequestDTO>())).ReturnsAsync((StorefrontCategoryNodeDTO?)null);
        categories.Setup(s => s.MoveAsync(It.IsAny<Guid>(), It.IsAny<MoveStorefrontCategoryRequestDTO>())).ThrowsAsync(new ArgumentException("invalid"));
        categories.Setup(s => s.SetActiveAsync(It.IsAny<Guid>(), false)).ReturnsAsync(node);
        categories.Setup(s => s.SetActiveAsync(It.IsAny<Guid>(), true)).ReturnsAsync((StorefrontCategoryNodeDTO?)null);
        categories.SetupSequence(s => s.AssignAsync(Platform, It.IsAny<IReadOnlyCollection<Guid>>(), It.IsAny<Guid>()))
            .ReturnsAsync(2)
            .ThrowsAsync(new ArgumentException("invalid"));
        var controller = Controller(Mock.Of<IStorefrontService>(), Mock.Of<IStorefrontCheckoutService>(), categories.Object);
        var id = Guid.NewGuid();

        Assert.IsType<UnauthorizedResult>(await controller.CategoryTree());
        SetUser(controller, Platform);
        Assert.IsType<OkObjectResult>(await controller.CategoryTree());
        Assert.IsType<NotFoundResult>(await controller.UpdateCategory(id, new()));
        Assert.IsType<BadRequestObjectResult>(await controller.MoveCategory(id, new()));
        Assert.IsType<OkObjectResult>(await controller.DeactivateCategory(id));
        Assert.IsType<NotFoundResult>(await controller.ActivateCategory(id));
        Assert.IsType<BadRequestObjectResult>(await controller.ProductCategory(null!));
        Assert.IsType<OkObjectResult>(await controller.ProductCategory(new() { CategoryId = id, ProductIds = [id, id] }));
        Assert.IsType<BadRequestObjectResult>(await controller.ProductCategory(new() { CategoryId = id, ProductIds = [id] }));
    }

    private static StorefrontController Controller(
        IStorefrontService storefront,
        IStorefrontCheckoutService checkout,
        IStorefrontCategoryService categories) =>
        new(storefront, checkout, categories, null!, null!, null!,
            new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Storefront:PlatformEnterpriseId"] = Platform.ToString()
            }).Build())
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };

    private static void SetUser(StorefrontController controller, Guid enterpriseId, Guid? userId = null) =>
        controller.ControllerContext.HttpContext.User = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim(ClaimTypes.GroupSid, enterpriseId.ToString()),
            new Claim("uid", (userId ?? Guid.NewGuid()).ToString())
        ], "test"));

    private static bool IsValid<T>(IValidator<T> validator, T value) => validator.Validate(value).IsValid;
}
