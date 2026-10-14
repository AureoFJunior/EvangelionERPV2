using EvangelionERPV2.Shared.Entities;
using Microsoft.EntityFrameworkCore;

namespace EvangelionERPV2.ProductModule.Application.Services;

public partial class ProductService
{
    public async Task<bool> DeletePictureAsync(Product product)
    {
        var coverId = await _appDbContext.ProductImage.AsNoTracking()
            .Where(i => i.ProductId == product.Id && i.IsActive == true)
            .OrderBy(i => i.Position).ThenBy(i => i.CreatedAt)
            .Select(i => (Guid?)i.Id).FirstOrDefaultAsync();
        if (coverId.HasValue && product.EnterpriseId is { } enterpriseId)
            return await RemoveImageAsync(enterpriseId, coverId.Value);

        if (string.IsNullOrWhiteSpace(product.PictureAdress)) return false;
        var address = product.PictureAdress;
        product.PictureAdress = null;
        product.PictureWidth = null;
        product.PictureHeight = null;
        product.UpdatedAt = DateTime.UtcNow;
        _productRepository.Update(product);
        await _productRepository.CommitAsync();
        await DeleteImageAsync(address);
        return true;
    }

    public async Task<bool> DeleteFamilyPictureAsync(Guid enterpriseId, Guid variantGroupId)
    {
        var family = await _appDbContext.ProductVariantGroup.FirstOrDefaultAsync(g =>
            g.Id == variantGroupId && g.EnterpriseId == enterpriseId && g.PictureAdress != null);
        if (family is null) return false;
        var address = family.PictureAdress;
        family.PictureAdress = null;
        family.UpdatedAt = DateTime.UtcNow;
        await _appDbContext.SaveChangesAsync();
        await DeleteImageAsync(address);
        return true;
    }

    public async Task<string> GetMediumPictureBase64Async(string? pictureAddress)
    {
        var (base64, _) = await GetThumbnailBase64Async(pictureAddress);
        return base64;
    }
}
