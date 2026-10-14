using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using EvangelionERPV2.ProductModule.Application.Interface;
using EvangelionERPV2.Shared.Context;
using EvangelionERPV2.Shared.Entities;
using EvangelionERPV2.Shared.Enums;
using EvangelionERPV2.Shared.Utils;
using EvangelionERPV2.StorefrontModule.Application.DTOs;
using EvangelionERPV2.StorefrontModule.Application.Interface;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace EvangelionERPV2.StorefrontModule.Application.Services
{
    public sealed partial class StorefrontService : IStorefrontService
    {
        /// <summary>Stock is shown as available + this cap, never as a count (spec §4.3).</summary>
        public const int MaxPerOrderCap = 10;

        public const int MaxProductIdsPerCall = 500;

        /// <summary>"Últimas unidades" at or below this many in stock — a flag, never the count (design-alive §4.3).</summary>
        public const int LowStockThreshold = 3;

        public const int BestSellingDays = 30;

        private readonly AppDbContext _context;
        private readonly IProductService<Product> _productService;
        private readonly Guid? _platformEnterpriseId;
        private readonly StorefrontInstallments _installments;

        public StorefrontService(AppDbContext context, IProductService<Product> productService, IConfiguration configuration)
        {
            _context = context;
            _productService = productService;
            _platformEnterpriseId = Guid.TryParse(configuration["Storefront:PlatformEnterpriseId"], out var id) ? id : null;
            _installments = new StorefrontInstallments(configuration);
        }

        #region Catalog

        private IQueryable<Enterprise> ListedEnterprises =>
            _context.Enterprise.AsNoTracking().Where(e => e.IsActive == true && e.IsListedOnStorefront && e.StorefrontSlug != null);

        private IQueryable<Product> PublishedProducts => Published(_context);

        internal static IQueryable<Product> Published(AppDbContext context) =>
            context.Product.AsNoTracking().Where(p =>
                p.IsActive == true && p.IsPublishedOnStorefront &&
                p.Enterprise != null && p.Enterprise.IsActive == true && p.Enterprise.IsListedOnStorefront && p.Enterprise.StorefrontSlug != null);

        public async Task<StorefrontPageDTO<StorefrontEnterpriseSummaryDTO>> GetEnterprisesAsync(int page, int size)
        {
            var total = await ListedEnterprises.CountAsync();
            var enterprises = await ListedEnterprises
                .OrderBy(e => e.Name).ThenBy(e => e.Id)
                .Skip((page - 1) * size).Take(size)
                .ToListAsync();

            var ids = enterprises.Select(e => e.Id).ToList();
            var fiscal = await FiscalByEnterpriseAsync(ids);
            var counts = await GroupCountsAsync(ids);

            var items = enterprises
                .Select(e => ToProfile(e, fiscal.GetValueOrDefault(e.Id), counts.GetValueOrDefault(e.Id)))
                .Select(p => new StorefrontEnterpriseSummaryDTO(p.Id, p.Slug, p.Name, p.LogoUrl, p.Description, p.City, p.Uf, p.ProductCount, p.LogoVersion, p.CoverVersion))
                .ToList();

            return new StorefrontPageDTO<StorefrontEnterpriseSummaryDTO>(items, total);
        }

        public async Task<StorefrontEnterpriseProfileDTO?> GetEnterpriseAsync(string slug)
        {
            var enterprise = await ListedEnterprises.FirstOrDefaultAsync(e => e.StorefrontSlug == slug);
            return enterprise == null ? null : await ProfileAsync(enterprise);
        }

        public async Task<StorefrontPageDTO<StorefrontProductSummaryDTO>> GetProductsAsync(StorefrontProductQuery query)
        {
            var source = PublishedProducts;
            var shopFilteredInSql = !query.Facets && !string.IsNullOrWhiteSpace(query.EnterpriseSlug);
            if (shopFilteredInSql)
                source = source.Where(p => p.Enterprise!.StorefrontSlug == query.EnterpriseSlug);
            if (!string.IsNullOrWhiteSpace(query.Category))
            {
                var covered = await StorefrontCategoryService.CoveredIdsAsync(_context, query.Category);
                source = source.Where(p => p.StorefrontCategoryId != null && covered.Contains(p.StorefrontCategoryId.Value));
            }

            // ponytail: the whole published catalog (filtered by shop) is grouped, searched and paged in
            // memory; the storefront caches the result for 60 s. Move grouping and the §6 search into SQL
            // when the published catalog passes ~20k rows or this query passes 300 ms at p95.
            var rows = await source.Select(p => new CatalogRow(
                    p.Id,
                    p.VariantGroupId ?? p.Id,
                    p.Name,
                    p.VariantLabel,
                    p.DefaultValue,
                    p.StorageQuantity,
                    p.IsService,
                    p.PictureAdress != null && p.PictureAdress != "",
                    p.CreatedAt,
                    p.Enterprise!.Name,
                    p.Enterprise.StorefrontSlug!,
                    p.EnterpriseId!.Value,
                    p.CompareAtPrice,
                    p.PromotionEndsAt,
                    p.Enterprise.StorefrontShippingFee,
                    p.StorefrontCategoryId))
                .ToListAsync();

            var sold = query.Sort == StorefrontProductSort.BestSelling ? CardUnits(rows, await UnitsSoldAsync()) : null;
            var familyPhotos = await FamiliesWithPictureAsync(rows.Where(r => r.GroupKey != r.Id).Select(r => r.GroupKey).Distinct().ToList());
            var words = string.IsNullOrWhiteSpace(query.Q) ? [] : SearchWords(query.Q);
            var foldedQuery = string.IsNullOrWhiteSpace(query.Q) ? null : Fold(query.Q.Trim());

            var candidates = rows
                // A group id is typed by the client, so two shops can share one: a card is per shop.
                .GroupBy(row => (row.GroupKey, row.EnterpriseId))
                .Where(group => words.Length == 0 || group.Any(row => words.All(Fold(row.SearchText).Contains)))
                .Select(group =>
                {
                    var card = ToCard(group, group.Key.GroupKey, familyPhotos.Contains(group.Key));
                    if (_installments.Plans(card.Price) is { Count: > 1 } plans)
                        card = card with { MaxInstallment = plans[^1] };
                    var first = group.First();
                    return new CatalogCard(card, first.ShippingFee == 0, group.Select(row => row.CategoryId).OfType<Guid>().Distinct().ToList());
                })
                .ToList();

            var facets = query.Facets ? await FacetsAsync(candidates) : null;

            var cards = candidates
                .Where(c =>
                    (shopFilteredInSql || string.IsNullOrWhiteSpace(query.EnterpriseSlug) || c.Card.EnterpriseSlug == query.EnterpriseSlug) &&
                    (query.MinPrice is null || c.Card.Price >= query.MinPrice) &&
                    (query.MaxPrice is null || c.Card.Price <= query.MaxPrice) &&
                    (!query.Available || c.Card.Available) &&
                    (!query.OnSale || c.Card.MaxDiscountPercent != null) &&
                    (!query.FreeShipping || c.FreeShipping) &&
                    (sold == null || sold.ContainsKey(c.Card.Id)))
                .Select(c => c.Card);

            var sorted = query.Sort switch
            {
                StorefrontProductSort.PriceAsc => cards.OrderBy(card => card.Price),
                StorefrontProductSort.PriceDesc => cards.OrderByDescending(card => card.Price),
                StorefrontProductSort.Newest => cards.OrderByDescending(card => card.CreatedAt),
                StorefrontProductSort.BestSelling => cards.OrderByDescending(card => sold!.GetValueOrDefault(card.Id)),
                _ when foldedQuery != null => cards
                    .OrderByDescending(card => Fold(card.Name).StartsWith(foldedQuery, StringComparison.Ordinal))
                    .ThenByDescending(card => card.CreatedAt),
                _ => cards.OrderByDescending(card => card.CreatedAt)
            };

            var matched = sorted.ThenBy(card => card.Id).ToList();
            var items = matched.Skip((query.Page - 1) * query.Size).Take(query.Size).ToList();

            return new StorefrontPageDTO<StorefrontProductSummaryDTO>(items, matched.Count, facets);
        }

        private sealed record CatalogCard(StorefrontProductSummaryDTO Card, bool FreeShipping, IReadOnlyList<Guid> CategoryIds);

        private async Task<StorefrontFacetsDTO> FacetsAsync(IReadOnlyList<CatalogCard> cards)
        {
            var categoryIds = cards.SelectMany(c => c.CategoryIds).Distinct().ToList();
            var slugs = categoryIds.Count == 0
                ? new Dictionary<Guid, string>()
                : await _context.StorefrontCategory.AsNoTracking()
                    .Where(c => categoryIds.Contains(c.Id) && c.IsActive == true)
                    .ToDictionaryAsync(c => c.Id, c => c.Slug);

            return new StorefrontFacetsDTO(
                cards.GroupBy(c => (c.Card.EnterpriseSlug, c.Card.EnterpriseName))
                    .Select(g => new StorefrontStoreFacetDTO(g.Key.EnterpriseSlug, g.Key.EnterpriseName, g.Count()))
                    .OrderByDescending(f => f.Count).ThenBy(f => f.Name, StringComparer.Ordinal)
                    .ToList(),
                cards.SelectMany(c => c.CategoryIds.Where(slugs.ContainsKey).Select(id => slugs[id]).Distinct())
                    .GroupBy(slug => slug)
                    .Select(g => new StorefrontCategoryFacetDTO(g.Key, g.Count()))
                    .OrderByDescending(f => f.Count).ThenBy(f => f.Slug, StringComparer.Ordinal)
                    .ToList(),
                cards.Count(c => c.FreeShipping),
                cards.Count(c => c.Card.MaxDiscountPercent != null));
        }

        public async Task<StorefrontProductDetailDTO?> GetProductAsync(Guid id)
        {
            var product = await PublishedProducts.Include(p => p.Enterprise).FirstOrDefaultAsync(p => p.Id == id);
            if (product == null)
                return null;

            var siblings = product.VariantGroupId is { } group
                ? await _context.Product.AsNoTracking()
                    .Where(p => p.VariantGroupId == group && p.EnterpriseId == product.EnterpriseId && p.IsActive == true)
                    .OrderBy(p => p.CreatedAt).ThenBy(p => p.Id)
                    .ToListAsync()
                : [product];

            var published = siblings.Where(p => p.IsPublishedOnStorefront).ToList();
            var variants = product.VariantGroupId == null
                ? []
                : siblings.Select(p => new StorefrontVariantDTO(
                        p.Id, p.VariantLabel ?? string.Empty, p.DefaultValue, p.IsPublishedOnStorefront && IsAvailable(p.IsService, p.StorageQuantity),
                        HasPicture: p.IsPublishedOnStorefront && !string.IsNullOrWhiteSpace(p.PictureAdress)))
                    .ToList();
            var familyPhoto = product.VariantGroupId is { } familyId && (await FamiliesWithPictureAsync([familyId])).Contains((familyId, product.EnterpriseId!.Value))
                ? familyId
                : (Guid?)null;

            var enterprise = await ProfileAsync(product.Enterprise!);
            var onSale = product.PromotionValidAt(DateTime.UtcNow);
            var categoryPath = product.StorefrontCategoryId is { } categoryId
                ? await StorefrontCategoryService.PathOfAsync(_context, categoryId)
                : [];
            var available = IsAvailable(product.IsService, product.StorageQuantity);

            return new StorefrontProductDetailDTO(
                product.Id,
                product.Name,
                product.DefaultValue,
                !string.IsNullOrWhiteSpace(product.PictureAdress),
                available,
                enterprise.Name,
                enterprise.Slug,
                product.CreatedAt,
                PriceFrom: published.Select(p => p.DefaultValue).Distinct().Count() > 1,
                VariantCount: published.Count,
                product.Description,
                MaxPerOrder: available ? MaxPerOrder(product.IsService, product.StorageQuantity) : 0,
                product.UnitOfMeasure,
                enterprise,
                variants,
                SelectedVariantId: product.Id,
                PictureProductId: string.IsNullOrWhiteSpace(product.PictureAdress) ? null : product.Id,
                FamilyPictureId: familyPhoto,
                CategoryPath: categoryPath,
                CompareAtPrice: onSale ? product.CompareAtPrice : null,
                DiscountPercent: onSale ? Product.DiscountPercent(product.CompareAtPrice!.Value, product.DefaultValue) : null,
                PromotionEndsAt: onSale ? product.PromotionEndsAt : null,
                LowStock: available && !product.IsService && product.StorageQuantity <= LowStockThreshold,
                Installments: _installments.Plans(product.DefaultValue));
        }

        public async Task<StorefrontPictureDTO?> GetPictureAsync(Guid productId)
        {
            var address = await PublishedProducts
                .Where(p => p.Id == productId)
                .Select(p => p.PictureAdress)
                .FirstOrDefaultAsync();

            var key = SharedFunctions.EnsureDecryptedAddress(address);
            if (string.IsNullOrWhiteSpace(key))
                return null;

            var base64 = await _productService.GetPictureBase64Async(key);
            if (string.IsNullOrWhiteSpace(base64))
                return null;

            var bytes = Convert.FromBase64String(base64);
            return new StorefrontPictureDTO(bytes, SniffImageType(bytes));
        }

        /// <summary>A published family's own photo (review spec, family photo).</summary>
        public async Task<StorefrontPictureDTO?> GetFamilyPictureAsync(Guid variantGroupId)
        {
            var publishers = await PublishedProducts
                .Where(p => p.VariantGroupId == variantGroupId)
                .Select(p => p.EnterpriseId!.Value)
                .Distinct()
                .ToListAsync();
            if (publishers.Count == 0)
                return null;

            var address = await _context.ProductVariantGroup.AsNoTracking()
                .Where(g => g.Id == variantGroupId && publishers.Contains(g.EnterpriseId))
                .Select(g => g.PictureAdress)
                .FirstOrDefaultAsync();
            if (string.IsNullOrWhiteSpace(address))
                return null;

            var base64 = await _productService.GetPictureBase64Async(address);
            if (string.IsNullOrWhiteSpace(base64))
                return null;

            var bytes = Convert.FromBase64String(base64);
            return new StorefrontPictureDTO(bytes, SniffImageType(bytes));
        }

        /// <summary>(group, enterprise) pairs with a family photo: the pair, never the group id alone, says whose photo it is.</summary>
        private async Task<HashSet<(Guid GroupKey, Guid EnterpriseId)>> FamiliesWithPictureAsync(IReadOnlyCollection<Guid> groupIds) =>
            groupIds.Count == 0
                ? []
                : (await _context.ProductVariantGroup.AsNoTracking()
                    .Where(g => groupIds.Contains(g.Id) && g.PictureAdress != null)
                    .Select(g => new { g.Id, g.EnterpriseId })
                    .ToListAsync())
                .Select(g => (g.Id, g.EnterpriseId))
                .ToHashSet();

        private async Task<StorefrontEnterpriseProfileDTO> ProfileAsync(Enterprise enterprise)
        {
            var fiscal = await FiscalByEnterpriseAsync([enterprise.Id]);
            var counts = await GroupCountsAsync([enterprise.Id]);
            return ToProfile(enterprise, fiscal.GetValueOrDefault(enterprise.Id), counts.GetValueOrDefault(enterprise.Id));
        }

        private async Task<Dictionary<Guid, EnterpriseFiscalSettings>> FiscalByEnterpriseAsync(IReadOnlyCollection<Guid> ids) =>
            (await _context.EnterpriseFiscalSettings.AsNoTracking()
                .Where(f => ids.Contains(f.EnterpriseId))
                .ToListAsync())
            .GroupBy(f => f.EnterpriseId)
            .ToDictionary(g => g.Key, g => g.First());

        /// <summary>Cards, not rows: a group of variants counts once.</summary>
        private async Task<Dictionary<Guid, int>> GroupCountsAsync(IReadOnlyCollection<Guid> ids) =>
            (await PublishedProducts
                .Where(p => ids.Contains(p.EnterpriseId!.Value))
                .Select(p => new { EnterpriseId = p.EnterpriseId!.Value, Key = p.VariantGroupId ?? p.Id })
                .Distinct()
                .ToListAsync())
            .GroupBy(x => x.EnterpriseId)
            .ToDictionary(g => g.Key, g => g.Count());

        private static StorefrontEnterpriseProfileDTO ToProfile(Enterprise e, EnterpriseFiscalSettings? fiscal, int productCount) =>
            new(
                e.Id,
                e.StorefrontSlug!,
                e.Name,
                e.StorefrontLogoUrl,
                e.StorefrontDescription ?? string.Empty,
                fiscal?.Municipio ?? string.Empty,
                fiscal?.Uf ?? string.Empty,
                productCount,
                fiscal?.RazaoSocial ?? string.Empty,
                fiscal?.Cnpj ?? string.Empty,
                e.Email,
                e.PhoneNumber,
                e.StorefrontShippingFee,
                e.StorefrontHandlingDays + e.StorefrontDeliveryDaysMin,
                e.StorefrontHandlingDays + e.StorefrontDeliveryDaysMax,
                DateOnly.FromDateTime(e.StorefrontListedAt ?? e.CreatedAt),
                ImageVersion(e.StorefrontLogoAdress),
                ImageVersion(e.StorefrontCoverAdress),
                e.StorefrontReplyTimeHours,
                e.StorefrontPolicies);

        /// <summary>A short token that changes whenever the stored image does, so caches keyed on it never go stale.</summary>
        internal static string? ImageVersion(string? address) =>
            string.IsNullOrWhiteSpace(address)
                ? null
                : Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(address)))[..12].ToLowerInvariant();

        /// <summary>
        /// A group is priced and filtered by its lowest price, available when any row is, and shown
        /// through its cheapest available row — its cheapest row when none is. Its photo is the
        /// family's own, else that row's, else the first variant that has one, so the card never
        /// shows the placeholder while any photo exists (review spec W3).
        /// </summary>
        private const int MaxSwatches = 5;

        private static StorefrontProductSummaryDTO ToCard(IEnumerable<CatalogRow> group, Guid groupKey, bool familyHasPicture)
        {
            var rows = group.ToList();
            var representative = rows
                .OrderByDescending(row => row.Available)
                .ThenBy(row => row.Price)
                .ThenBy(row => row.CreatedAt)
                .ThenBy(row => row.Id)
                .First();

            var discounts = rows.Where(row => row.OnSale).Select(row => Product.DiscountPercent(row.CompareAtPrice!.Value, row.Price)).ToList();
            var floor = rows.Where(row => row.OnSale).OrderBy(row => row.Price).ThenByDescending(row => row.CompareAtPrice).FirstOrDefault();
            var floorOnSale = floor != null && floor.Price == rows.Min(row => row.Price);

            return new StorefrontProductSummaryDTO(
                representative.Id,
                representative.Name,
                rows.Min(row => row.Price),
                familyHasPicture || rows.Any(row => row.HasPicture),
                rows.Any(row => row.Available),
                representative.EnterpriseName,
                representative.EnterpriseSlug,
                rows.Max(row => row.CreatedAt),
                PriceFrom: rows.Select(row => row.Price).Distinct().Count() > 1,
                VariantCount: rows.Count,
                PictureProductId: representative.HasPicture
                    ? representative.Id
                    : rows.OrderBy(row => row.CreatedAt).ThenBy(row => row.Id).FirstOrDefault(row => row.HasPicture)?.Id,
                FamilyPictureId: familyHasPicture ? groupKey : null,
                CompareAtPrice: floorOnSale ? floor!.CompareAtPrice : null,
                DiscountPercent: floorOnSale ? Product.DiscountPercent(floor!.CompareAtPrice!.Value, floor.Price) : null,
                MaxDiscountPercent: discounts.Count > 0 ? discounts.Max() : null,
                LowStock: rows.Any(row => row.Available) && rows.Where(row => row.Available).All(row => !row.IsService) &&
                          rows.Where(row => row.Available).Sum(row => row.Stock) <= LowStockThreshold,
                Swatches: rows.Count > 1
                    ? rows.OrderBy(row => row.CreatedAt).ThenBy(row => row.Id).Take(MaxSwatches)
                        .Select(row => new StorefrontSwatchDTO(row.Id, row.Label ?? string.Empty, row.HasPicture)).ToList()
                    : null);
        }

        /// <summary>A family sells as one card: its rows' units add up, and every row id maps to the total, so the card's representative finds it. Unsold families are absent.</summary>
        private static Dictionary<Guid, decimal> CardUnits(IEnumerable<CatalogRow> rows, Dictionary<Guid, decimal> soldByRow) =>
            rows.GroupBy(row => (row.GroupKey, row.EnterpriseId))
                .Select(group => (Rows: group.ToList(), Units: group.Sum(row => soldByRow.GetValueOrDefault(row.Id))))
                .Where(family => family.Units > 0)
                .SelectMany(family => family.Rows.Select(row => (row.Id, family.Units)))
                .ToDictionary(x => x.Id, x => x.Units);

        /// <summary>Units sold on Limen in the last <see cref="BestSellingDays"/> days, by product row; refunded orders do not count.</summary>
        private async Task<Dictionary<Guid, decimal>> UnitsSoldAsync()
        {
            var since = DateTime.UtcNow.AddDays(-BestSellingDays);
            var byRow = await _context.OrderedProduct.AsNoTracking()
                .Where(line => line.Order!.Channel == (int)EnumOrderChannel.Storefront &&
                               line.Order.Status != (int)EnumOrderStatus.Refund &&
                               line.Order.CreatedAt >= since)
                .GroupBy(line => line.ProductId)
                .Select(g => new { ProductId = g.Key, Units = g.Sum(line => line.Quantity) })
                .ToListAsync();
            return byRow.ToDictionary(x => x.ProductId, x => x.Units);
        }

        private sealed record CatalogRow(
            Guid Id,
            Guid GroupKey,
            string Name,
            string? Label,
            decimal Price,
            decimal Stock,
            bool IsService,
            bool HasPicture,
            DateTime CreatedAt,
            string EnterpriseName,
            string EnterpriseSlug,
            Guid EnterpriseId,
            decimal? CompareAtPrice = null,
            DateTime? PromotionEndsAt = null,
            decimal ShippingFee = 0,
            Guid? CategoryId = null)
        {
            public bool Available => IsAvailable(IsService, Stock);

            /// <summary>Mirrors <see cref="Product.PromotionValidAt"/> on the projected row.</summary>
            public bool OnSale => CompareAtPrice > Price && PromotionEndsAt > DateTime.UtcNow;
            public string SearchText => Label is null ? Name : $"{Name} {Label}";
        }

        internal static bool IsAvailable(bool isService, decimal stock) => isService || stock >= 1;

        internal static int MaxPerOrder(bool isService, decimal stock) =>
            isService ? MaxPerOrderCap : (int)Math.Min(MaxPerOrderCap, Math.Floor(stock));

        /// <summary>
        /// Spec §6: every word must appear, in any order, ignoring case and accents; a trailing "s"
        /// is dropped from words over 3 letters.
        /// </summary>
        public static string[] SearchWords(string query) =>
            Fold(query)
                .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(word => word.Length > 3 && word.EndsWith('s') ? word[..^1] : word)
                .ToArray();

        private static string Fold(string text)
        {
            var decomposed = text.Normalize(NormalizationForm.FormD);
            var builder = new StringBuilder(decomposed.Length);
            foreach (var c in decomposed)
            {
                if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                    builder.Append(c);
            }

            return builder.ToString().ToLowerInvariant();
        }

        private static string SniffImageType(byte[] bytes) => bytes switch
        {
            [0x89, 0x50, 0x4E, 0x47, ..] => "image/png",
            [(byte)'R', (byte)'I', (byte)'F', (byte)'F', _, _, _, _, (byte)'W', (byte)'E', (byte)'B', (byte)'P', ..] => "image/webp",
            [(byte)'G', (byte)'I', (byte)'F', ..] => "image/gif",
            _ => "image/jpeg"
        };

        #endregion

        #region Listing (ERP staff)

        public async Task<StorefrontListingDTO> GetListingAsync(Guid enterpriseId)
        {
            var enterprise = await _context.Enterprise.AsNoTracking().FirstAsync(e => e.Id == enterpriseId);
            return await ToListingAsync(enterprise);
        }

        public async Task<StorefrontListingDTO> SetListingAsync(Guid enterpriseId, SetStorefrontListingRequestDTO request)
        {
            var enterprise = await _context.Enterprise.FirstAsync(e => e.Id == enterpriseId);

            var slug = string.IsNullOrWhiteSpace(request.Slug) ? null : request.Slug.Trim().ToLowerInvariant();
            if (slug != null && !SlugPattern().IsMatch(slug))
                throw new ArgumentException("O endereço da loja deve ter de 3 a 60 letras minúsculas, números e hífens.");

            if (slug != null && await _context.Enterprise.AnyAsync(e => e.StorefrontSlug == slug && e.Id != enterpriseId))
                throw new ArgumentException("Esse endereço de loja já está em uso.");

            var description = request.Description?.Trim();
            if (description?.Length > 500)
                throw new ArgumentException("A descrição pode ter até 500 caracteres.");

            var logoUrl = string.IsNullOrWhiteSpace(request.LogoUrl) ? null : request.LogoUrl.Trim();
            if (logoUrl != null && (logoUrl.Length > 500 || !Uri.TryCreate(logoUrl, UriKind.Absolute, out var logo) || logo.Scheme != Uri.UriSchemeHttps))
                throw new ArgumentException("O logo deve ser um endereço https.");

            if (request.ReplyTimeHours is < 1 or > 168)
                throw new ArgumentException("O tempo de resposta deve ser de 1 a 168 horas.");

            var policies = string.IsNullOrWhiteSpace(request.Policies) ? null : request.Policies.Trim();
            if (policies?.Length > 4000)
                throw new ArgumentException("As políticas podem ter até 4.000 caracteres.");

            if (request.ShippingFee is < 0 or > 9999)
                throw new ArgumentException("O frete deve estar entre R$ 0 e R$ 9.999.");

            if (request.HandlingDays is < 0 or > 30 ||
                request.DeliveryDaysMin is < 0 or > 90 ||
                request.DeliveryDaysMax < request.DeliveryDaysMin || request.DeliveryDaysMax > 90)
                throw new ArgumentException("Prazos inválidos: postagem de 0 a 30 dias úteis, transporte de 0 a 90, mínimo até o máximo.");

            enterprise.StorefrontSlug = slug;
            enterprise.StorefrontDescription = description;
            enterprise.StorefrontLogoUrl = logoUrl;
            enterprise.StorefrontShippingFee = decimal.Round(request.ShippingFee, 2);
            enterprise.StorefrontHandlingDays = request.HandlingDays;
            enterprise.StorefrontDeliveryDaysMin = request.DeliveryDaysMin;
            enterprise.StorefrontDeliveryDaysMax = request.DeliveryDaysMax;
            enterprise.StorefrontReplyTimeHours = request.ReplyTimeHours;
            enterprise.StorefrontPolicies = policies;

            if (request.IsListed)
            {
                var missing = await MissingRequirementsAsync(enterprise);
                if (missing.Count > 0)
                    throw new ArgumentException("A loja ainda não pode ser listada: " + string.Join(" ", missing));

                enterprise.StorefrontListedAt ??= DateTime.UtcNow;
            }

            enterprise.IsListedOnStorefront = request.IsListed;
            enterprise.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();

            return await ToListingAsync(enterprise);
        }

        private const long MaxListingImageBytes = 5 * 1024 * 1024;
        private const string ListingImageRule = "A imagem deve ser PNG, JPEG ou WebP de até 5 MB.";

        public async Task<StorefrontListingDTO> SetListingImageAsync(Guid enterpriseId, StorefrontImageKind kind, string? base64File)
        {
            byte[] image;
            try
            {
                using var stream = SharedFunctions.GetMemoryStreamFromBase64Payload(base64File ?? string.Empty, MaxListingImageBytes);
                image = stream.ToArray();
            }
            catch (ArgumentException)
            {
                throw new ArgumentException(ListingImageRule);
            }

            if (!IsAcceptedImage(image))
                throw new ArgumentException(ListingImageRule);

            var enterprise = await _context.Enterprise.FirstAsync(e => e.Id == enterpriseId);
            var address = await _productService.StoreImageAsync(
                kind == StorefrontImageKind.Logo ? "storefront-logos" : "storefront-covers", enterpriseId, image);
            var previous = SetImageAddress(enterprise, kind, address);

            try
            {
                await _context.SaveChangesAsync();
            }
            catch
            {
                await _productService.DeleteImageAsync(address);
                throw;
            }

            await _productService.DeleteImageAsync(previous);
            return await ToListingAsync(enterprise);
        }

        public async Task<StorefrontListingDTO> RemoveListingImageAsync(Guid enterpriseId, StorefrontImageKind kind)
        {
            var enterprise = await _context.Enterprise.FirstAsync(e => e.Id == enterpriseId);
            var previous = SetImageAddress(enterprise, kind, null);
            if (previous != null)
            {
                await _context.SaveChangesAsync();
                await _productService.DeleteImageAsync(previous);
            }

            return await ToListingAsync(enterprise);
        }

        public Task<StorefrontPictureDTO?> GetEnterpriseImageAsync(string slug, StorefrontImageKind kind) =>
            LoadImageAsync(ListedEnterprises.Where(e => e.StorefrontSlug == slug), kind);

        public Task<StorefrontPictureDTO?> GetListingImageAsync(Guid enterpriseId, StorefrontImageKind kind) =>
            LoadImageAsync(_context.Enterprise.AsNoTracking().Where(e => e.Id == enterpriseId), kind);

        private async Task<StorefrontPictureDTO?> LoadImageAsync(IQueryable<Enterprise> enterprise, StorefrontImageKind kind)
        {
            var address = await enterprise
                .Select(e => kind == StorefrontImageKind.Logo ? e.StorefrontLogoAdress : e.StorefrontCoverAdress)
                .FirstOrDefaultAsync();
            if (string.IsNullOrWhiteSpace(address))
                return null;

            var base64 = await _productService.GetPictureBase64Async(address);
            if (string.IsNullOrWhiteSpace(base64))
                return null;

            var bytes = Convert.FromBase64String(base64);
            return new StorefrontPictureDTO(bytes, SniffImageType(bytes));
        }

        private static string? SetImageAddress(Enterprise enterprise, StorefrontImageKind kind, string? address)
        {
            string? previous;
            if (kind == StorefrontImageKind.Logo)
            {
                previous = enterprise.StorefrontLogoAdress;
                enterprise.StorefrontLogoAdress = address;
            }
            else
            {
                previous = enterprise.StorefrontCoverAdress;
                enterprise.StorefrontCoverAdress = address;
            }

            enterprise.UpdatedAt = DateTime.UtcNow;
            return previous;
        }

        private static bool IsAcceptedImage(byte[] bytes) => bytes switch
        {
            [0x89, 0x50, 0x4E, 0x47, ..] => true,
            [0xFF, 0xD8, 0xFF, ..] => true,
            [(byte)'R', (byte)'I', (byte)'F', (byte)'F', _, _, _, _, (byte)'W', (byte)'E', (byte)'B', (byte)'P', ..] => true,
            _ => false
        };

        public async Task<int> SetPublishedAsync(Guid enterpriseId, IReadOnlyCollection<Guid> productIds, bool published)
        {
            if (productIds.Count == 0)
                return 0;

            if (productIds.Count > MaxProductIdsPerCall)
                throw new ArgumentException($"No máximo {MaxProductIdsPerCall} produtos por vez.");

            var products = await _context.Product
                .Where(p => p.EnterpriseId == enterpriseId && productIds.Contains(p.Id) && p.IsPublishedOnStorefront != published)
                .ToListAsync();

            var uncategorized = published ? products.Where(p => p.StorefrontCategoryId == null).Select(p => p.Name).Distinct().ToList() : [];
            if (uncategorized.Count > 0)
                throw new ArgumentException("Escolha a categoria antes de publicar: " + string.Join(", ", uncategorized.Take(10)) +
                                            (uncategorized.Count > 10 ? $" e mais {uncategorized.Count - 10}." : "."));

            foreach (var product in products)
            {
                product.IsPublishedOnStorefront = published;
                product.UpdatedAt = DateTime.UtcNow;
            }

            await _context.SaveChangesAsync();
            return products.Count;
        }

        private async Task<StorefrontListingDTO> ToListingAsync(Enterprise enterprise) => new()
        {
            IsListed = enterprise.IsListedOnStorefront,
            ListedAt = enterprise.StorefrontListedAt,
            Slug = enterprise.StorefrontSlug,
            Description = enterprise.StorefrontDescription,
            LogoUrl = enterprise.StorefrontLogoUrl,
            ShippingFee = enterprise.StorefrontShippingFee,
            HandlingDays = enterprise.StorefrontHandlingDays,
            DeliveryDaysMin = enterprise.StorefrontDeliveryDaysMin,
            DeliveryDaysMax = enterprise.StorefrontDeliveryDaysMax,
            LogoVersion = ImageVersion(enterprise.StorefrontLogoAdress),
            CoverVersion = ImageVersion(enterprise.StorefrontCoverAdress),
            ReplyTimeHours = enterprise.StorefrontReplyTimeHours,
            Policies = enterprise.StorefrontPolicies,
            MissingRequirements = await MissingRequirementsAsync(enterprise)
        };

        /// <summary>
        /// Listing puts the shop's identity and terms in front of the public (Decreto 7.962 art. 2),
        /// and its money through Stripe (spec D2), so each of these must be in place first.
        /// </summary>
        private async Task<List<string>> MissingRequirementsAsync(Enterprise enterprise)
        {
            var missing = new List<string>();

            if (enterprise.Id == _platformEnterpriseId)
                missing.Add("A empresa da plataforma nunca é listada.");

            if (string.IsNullOrWhiteSpace(enterprise.StorefrontSlug))
                missing.Add("Defina o endereço da loja.");

            if (enterprise.StorefrontDeliveryDaysMax < 1)
                missing.Add("Defina o prazo de entrega.");

            if (string.IsNullOrWhiteSpace(enterprise.StripeConnectedAccountId))
                missing.Add("Conecte uma conta Stripe para receber os pagamentos.");

            var fiscal = await _context.EnterpriseFiscalSettings.AsNoTracking()
                .FirstOrDefaultAsync(f => f.EnterpriseId == enterprise.Id);

            if (fiscal == null || string.IsNullOrWhiteSpace(fiscal.RazaoSocial) || string.IsNullOrWhiteSpace(fiscal.Cnpj) ||
                string.IsNullOrWhiteSpace(fiscal.Municipio) || string.IsNullOrWhiteSpace(fiscal.Uf))
                missing.Add("Preencha razão social, CNPJ, município e UF nos dados fiscais.");

            return missing;
        }

        [GeneratedRegex("^[a-z0-9](?:[a-z0-9-]{1,58})[a-z0-9]$", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
        private static partial Regex SlugPattern();

        #endregion
    }
}
