using EvangelionERPV2.StorefrontModule.Application.DTOs;

namespace EvangelionERPV2.StorefrontModule.Test
{
    /// <summary>
    /// Everything in these DTOs is served to the public internet. A new property fails here on purpose:
    /// update the list only after checking it is none of the fields spec §4.3 forbids (cost, fiscal,
    /// exact stock, balances, Stripe ids, credit limits, the ERP user).
    /// </summary>
    public class StorefrontDtoSnapshotTests
    {
        [Theory]
        [InlineData(typeof(StorefrontEnterpriseSummaryDTO),
            "City,CoverVersion,Description,Id,LogoUrl,LogoVersion,Name,ProductCount,Slug,Uf")]
        [InlineData(typeof(StorefrontEnterpriseProfileDTO),
            "City,Cnpj,CoverVersion,DeliveryDaysMax,DeliveryDaysMin,Description,Email,Id,LegalName,ListedSince,LogoUrl,LogoVersion,Name,Phone,Policies,ProductCount,ReplyTimeHours,ShippingFee,Slug,Uf")]
        [InlineData(typeof(StorefrontProductSummaryDTO),
            "Available,CompareAtPrice,CreatedAt,DiscountPercent,EnterpriseName,EnterpriseSlug,FamilyPictureId,HasPicture,Id,LowStock,MaxDiscountPercent,MaxInstallment,Name,PictureProductId,Price,PriceFrom,Swatches,VariantCount")]
        [InlineData(typeof(StorefrontProductDetailDTO),
            "Available,CategoryPath,CompareAtPrice,CreatedAt,Description,DiscountPercent,Enterprise,EnterpriseName,EnterpriseSlug,FamilyPictureId,HasPicture,Id,Installments,LowStock,MaxDiscountPercent,MaxInstallment,MaxPerOrder,Name,PictureProductId,Price,PriceFrom,PromotionEndsAt,SelectedVariantId,Swatches,UnitOfMeasure,VariantCount,Variants")]
        [InlineData(typeof(StorefrontFacetsDTO),
            "Categories,FreeShippingCount,OnSaleCount,Stores")]
        [InlineData(typeof(StorefrontStoreFacetDTO),
            "Count,Name,Slug")]
        [InlineData(typeof(StorefrontCategoryFacetDTO),
            "Count,Slug")]
        [InlineData(typeof(StorefrontSwatchDTO),
            "HasPicture,Id,Label")]
        [InlineData(typeof(StorefrontPublicCategoryDTO),
            "Children,Description,Icon,Name,ProductCount,Slug")]
        [InlineData(typeof(StorefrontCategoryPathItemDTO),
            "Name,Slug")]
        [InlineData(typeof(StorefrontCategoryPageDTO),
            "Category,Path,RedirectTo")]
        [InlineData(typeof(StorefrontInstallmentDTO),
            "CetAnnual,Count,MonthlyRate,Total,Value")]
        [InlineData(typeof(StorefrontVariantDTO),
            "Available,HasPicture,Id,Label,Price")]
        [InlineData(typeof(StorefrontQuoteLineDTO),
            "FamilyPictureId,HasPicture,Issue,LineTotal,Name,ProductId,Quantity,UnitPrice")]
        [InlineData(typeof(StorefrontOutcomeDTO),
            "Amount,ContactEmail,DeliveryDaysMax,DeliveryDaysMin,Lines,Name,OrderNumber,Outcome,Slug")]
        [InlineData(typeof(StorefrontCheckoutDTO),
            "CreatedAt,CustomerName,DeclineCode,Email,Id,Installment,Outcomes,PaymentMethod,Pix,Quote,Simulated,Status,Total")]
        [InlineData(typeof(StorefrontOrderDTO),
            "Amount,CanWithdraw,CheckoutId,ContactEmail,DeliveredAt,DeliveryCity,DeliveryDaysMax,DeliveryDaysMin,DeliveryUf,InvoiceAccessKey,Lines,OrderNumber,PaidAt,PaymentMethod,ShippedAt,Status,StoreName,StoreSlug,TrackingCode,TrackingUrl,WithdrawalProtocol,WithdrawalRequestedAt")]
        [InlineData(typeof(StorefrontPurchaseDTO),
            "CheckoutId,Installment,Orders,PaidAt,PaymentMethod,Refunds,Total")]
        public void PublicDto_ExposesExactlyTheseProperties(Type dto, string expected)
        {
            var actual = string.Join(",", dto.GetProperties()
                .Where(property => property.Name != "EqualityContract")
                .Select(property => property.Name)
                .OrderBy(name => name, StringComparer.Ordinal));

            Assert.Equal(expected, actual);
        }
    }
}
