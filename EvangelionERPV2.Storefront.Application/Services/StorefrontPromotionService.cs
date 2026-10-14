using System.Globalization;
using EvangelionERPV2.Shared.Context;
using EvangelionERPV2.Shared.Entities;
using EvangelionERPV2.StorefrontModule.Application.DTOs;
using Microsoft.EntityFrameworkCore;

namespace EvangelionERPV2.StorefrontModule.Application.Services
{
    /// <summary>
    /// "De/por" on Limen (SPEC-limen-pricing-rules §2.1). P1–P4 and P7 are checked here, P5 by the price
    /// update itself, and P4's expiry on every read (<see cref="Product.PromotionValidAt"/>), so a lapsed
    /// promotion never shows even before anyone clears it. Refusals are <see cref="ArgumentException"/>
    /// in pt-BR naming the rule that failed.
    /// </summary>
    public sealed class StorefrontPromotionService(AppDbContext context, TimeProvider? clock = null)
    {
        public const int MinConsecutiveDays = 7;
        public const int WindowDays = 30;
        public const int MaxPromotionDays = 60;
        public const int MinDiscountPercent = 5;

        private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");
        private readonly TimeProvider _clock = clock ?? TimeProvider.System;

        public async Task<StorefrontPromotionDTO?> GetAsync(Guid enterpriseId, Guid productId)
        {
            var product = await context.Product.AsNoTracking().FirstOrDefaultAsync(p => p.Id == productId && p.EnterpriseId == enterpriseId);
            return product == null ? null : await ToDtoAsync(product);
        }

        public async Task<StorefrontPromotionDTO?> SetAsync(Guid enterpriseId, Guid userId, SetStorefrontPromotionRequestDTO request)
        {
            var product = await context.Product.FirstOrDefaultAsync(p => p.Id == request.ProductId && p.EnterpriseId == enterpriseId && p.IsActive == true);
            if (product == null)
                return null;

            var now = _clock.GetUtcNow().UtcDateTime;
            var compareAt = decimal.Round(request.CompareAtPrice, 2);

            if (compareAt <= product.DefaultValue)
                throw new ArgumentException($"O preço \"de\" precisa ser maior que o preço atual de {Money(product.DefaultValue)}.");

            if (Product.DiscountPercent(compareAt, product.DefaultValue) < MinDiscountPercent)
                throw new ArgumentException($"O desconto precisa ser de pelo menos {MinDiscountPercent}% para aparecer como promoção.");

            if (!(await EligiblePricesAsync(product.Id, product.DefaultValue, now)).Contains(compareAt))
                throw new ArgumentException(
                    $"O preço de {Money(compareAt)} não foi praticado por {MinConsecutiveDays} dias seguidos nos últimos {WindowDays} dias.");

            if (request.EndsAt <= now || request.EndsAt > now.AddDays(MaxPromotionDays))
                throw new ArgumentException($"A promoção precisa terminar em até {MaxPromotionDays} dias a partir de hoje.");

            if (!request.Attested)
                throw new ArgumentException($"Confirme que praticou o preço de {Money(compareAt)} nos últimos {WindowDays} dias.");

            product.CompareAtPrice = compareAt;
            product.PromotionStartedAt = now;
            product.PromotionEndsAt = request.EndsAt;
            product.PromotionAttestedByUserId = userId;
            product.PromotionAttestedAt = now;
            product.UpdatedAt = now;
            await context.SaveChangesAsync();
            return await ToDtoAsync(product);
        }

        public async Task<StorefrontPromotionDTO?> EndAsync(Guid enterpriseId, Guid productId)
        {
            var product = await context.Product.FirstOrDefaultAsync(p => p.Id == productId && p.EnterpriseId == enterpriseId);
            if (product == null)
                return null;

            product.EndPromotion();
            product.UpdatedAt = _clock.GetUtcNow().UtcDateTime;
            await context.SaveChangesAsync();
            return await ToDtoAsync(product);
        }

        /// <summary>
        /// P2: prices this product held for <see cref="MinConsecutiveDays"/> days in a row inside the last
        /// <see cref="WindowDays"/> days, above its current price by at least P3's discount.
        /// </summary>
        public async Task<IReadOnlyList<decimal>> EligiblePricesAsync(Guid productId, decimal currentPrice, DateTime now)
        {
            var windowStart = now.AddDays(-WindowDays);
            var rows = await context.ProductPriceHistory.AsNoTracking()
                .Where(row => row.ProductId == productId && (row.To == null || row.To > windowStart))
                .OrderBy(row => row.From)
                .ToListAsync();

            // Adjacent rows at the same price are one stretch: a no-op save must not break a run.
            var runs = new List<(decimal Price, DateTime From, DateTime To)>();
            foreach (var row in rows)
            {
                var to = row.To ?? now;
                if (runs.Count > 0 && runs[^1].Price == row.Price && runs[^1].To >= row.From)
                    runs[^1] = (row.Price, runs[^1].From, to);
                else
                    runs.Add((row.Price, row.From, to));
            }

            return runs
                .Where(run => (Min(run.To, now) - Max(run.From, windowStart)).TotalDays >= MinConsecutiveDays)
                .Select(run => run.Price)
                .Where(price => price > currentPrice && Product.DiscountPercent(price, currentPrice) >= MinDiscountPercent)
                .Distinct()
                .OrderDescending()
                .ToList();
        }

        private async Task<StorefrontPromotionDTO> ToDtoAsync(Product product)
        {
            var now = _clock.GetUtcNow().UtcDateTime;
            var active = product.PromotionValidAt(now);
            return new StorefrontPromotionDTO(
                product.Id,
                product.DefaultValue,
                product.CompareAtPrice,
                active ? Product.DiscountPercent(product.CompareAtPrice!.Value, product.DefaultValue) : null,
                product.PromotionStartedAt,
                product.PromotionEndsAt,
                active,
                await EligiblePricesAsync(product.Id, product.DefaultValue, now));
        }

        private static DateTime Min(DateTime a, DateTime b) => a < b ? a : b;
        private static DateTime Max(DateTime a, DateTime b) => a > b ? a : b;
        private static string Money(decimal value) => value.ToString("C", PtBr);
    }
}
