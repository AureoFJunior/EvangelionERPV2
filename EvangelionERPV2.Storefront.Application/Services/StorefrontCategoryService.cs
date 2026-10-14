using System.Text.RegularExpressions;
using EvangelionERPV2.ProductModule.Application.Services;
using EvangelionERPV2.Shared.Context;
using EvangelionERPV2.Shared.Entities;
using EvangelionERPV2.StorefrontModule.Application.DTOs;
using EvangelionERPV2.StorefrontModule.Application.Interface;
using Microsoft.EntityFrameworkCore;

namespace EvangelionERPV2.StorefrontModule.Application.Services
{
    /// <summary>
    /// Limen's category tree (SPEC-limen-categories.md): one tree for the platform, edited by the Limen
    /// team; products are filed under a leaf, and a variant family moves as one. Refusals are
    /// <see cref="ArgumentException"/> with a pt-BR message, so the API answers 400 with it.
    /// </summary>
    public sealed partial class StorefrontCategoryService : IStorefrontCategoryService
    {
        public const int MaxProductsPerAssignment = 500;

        /// <summary>The Phosphor icons a category may use; the storefront renders exactly these.</summary>
        public static readonly IReadOnlySet<string> Icons = new HashSet<string>(StringComparer.Ordinal)
        {
            "Tag", "CookingPot", "Coffee", "BowlFood", "Knife", "Lamp", "Towel", "CoffeeBean", "Leaf", "Cookie", "Pepper",
            "BeerBottle", "Notebook", "Pencil", "PaintBrush", "Folders", "TShirt", "Backpack", "Diamond", "Sunglasses",
            "Sparkle", "Flower", "Drop", "PaintBrushHousehold", "Jar", "FrameCorners", "Palette", "Books", "Book",
            "Newspaper", "Barbell", "PuzzlePiece", "Baby", "Cube", "PawPrint", "Dog", "Cat", "Plant", "Shovel",
            "DeviceMobile", "Headphones", "Gift", "Watch", "Car", "Wrench", "FirstAid", "MusicNotes", "GameController"
        };

        private readonly AppDbContext _context;

        public StorefrontCategoryService(AppDbContext context)
        {
            _context = context;
        }

        #region Staff (ERP)

        /// <param name="includeInactive">The Limen team's view: inactive nodes too, and counts across every store.</param>
        public async Task<IReadOnlyList<StorefrontCategoryNodeDTO>> GetTreeAsync(Guid enterpriseId, bool includeInactive)
        {
            var nodes = await _context.StorefrontCategory.AsNoTracking().ToListAsync();
            var products = includeInactive ? _context.Product.IgnoreQueryFilters() : _context.Product.Where(p => p.EnterpriseId == enterpriseId);
            var filed = await products.AsNoTracking()
                .Where(p => p.IsActive == true && p.StorefrontCategoryId != null)
                .Select(p => new { Category = p.StorefrontCategoryId!.Value, Enterprise = p.EnterpriseId!.Value })
                .ToListAsync();

            (int Products, HashSet<Guid> Stores) Count(Guid id) =>
                (filed.Count(f => f.Category == id), filed.Where(f => f.Category == id).Select(f => f.Enterprise).ToHashSet());

            (StorefrontCategoryNodeDTO Node, HashSet<Guid> Stores) Build(StorefrontCategory node)
            {
                var children = nodes
                    .Where(n => n.ParentId == node.Id && (includeInactive || n.IsActive == true))
                    .OrderBy(n => n.SortOrder).ThenBy(n => n.Name)
                    .Select(Build)
                    .ToList();
                var own = Count(node.Id);
                var stores = children.SelectMany(c => c.Stores).Concat(own.Stores).ToHashSet();
                return (new StorefrontCategoryNodeDTO(node.Id, node.ParentId, node.Name, node.Slug, node.Icon, node.Description, node.SortOrder,
                    node.IsActive == true, own.Products + children.Sum(c => c.Node.ProductCount), stores.Count, children.Select(c => c.Node).ToList()), stores);
            }

            return nodes
                .Where(n => n.ParentId == null && (includeInactive || n.IsActive == true))
                .OrderBy(n => n.SortOrder).ThenBy(n => n.Name)
                .Select(n => Build(n).Node)
                .ToList();
        }

        public async Task<StorefrontCategoryNodeDTO> CreateAsync(SaveStorefrontCategoryRequestDTO request)
        {
            var (name, slug, description) = await ValidateAsync(request, request.ParentId, existingId: null);
            if (request.ParentId is { } parentId)
                await EnsureCanHoldChildrenAsync(parentId);

            var node = new StorefrontCategory
            {
                Id = Guid.NewGuid(),
                ParentId = request.ParentId,
                Name = name,
                Slug = slug,
                Icon = request.Icon,
                Description = description,
                SortOrder = request.SortOrder,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
                IsActive = true
            };
            _context.StorefrontCategory.Add(node);
            await _context.SaveChangesAsync();
            return ToNode(node);
        }

        public async Task<StorefrontCategoryNodeDTO?> UpdateAsync(Guid id, SaveStorefrontCategoryRequestDTO request)
        {
            var node = await _context.StorefrontCategory.FirstOrDefaultAsync(n => n.Id == id);
            if (node == null)
                return null;

            var (name, slug, description) = await ValidateAsync(request, node.ParentId, existingId: id);
            if (!string.Equals(node.Slug, slug, StringComparison.Ordinal))
            {
                // C7: the old slug keeps working as a redirect; a slug coming back to this node is no longer an alias.
                _context.StorefrontCategorySlugAlias.Add(new StorefrontCategorySlugAlias
                {
                    Id = Guid.NewGuid(), OldSlug = node.Slug, CategoryId = node.Id, CreatedAt = DateTime.UtcNow, IsActive = true
                });
                var returning = await _context.StorefrontCategorySlugAlias.Where(a => a.OldSlug == slug && a.CategoryId == id).ToListAsync();
                _context.StorefrontCategorySlugAlias.RemoveRange(returning);
            }

            node.Name = name;
            node.Slug = slug;
            node.Icon = request.Icon;
            node.Description = description;
            node.SortOrder = request.SortOrder;
            node.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();
            return ToNode(node);
        }

        public async Task<StorefrontCategoryNodeDTO?> MoveAsync(Guid id, MoveStorefrontCategoryRequestDTO request)
        {
            var node = await _context.StorefrontCategory.FirstOrDefaultAsync(n => n.Id == id);
            if (node == null)
                return null;

            if (request.ParentId is { } parentId)
            {
                if (parentId == id || await IsDescendantAsync(parentId, of: id))
                    throw new ArgumentException("Uma categoria não pode ficar dentro dela mesma.");
                await EnsureCanHoldChildrenAsync(parentId);
            }
            if (await _context.StorefrontCategory.AnyAsync(n => n.ParentId == request.ParentId && n.Name == node.Name && n.Id != id))
                throw new ArgumentException("Já existe uma categoria com esse nome aqui.");

            node.ParentId = request.ParentId;
            node.SortOrder = request.SortOrder;
            node.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();
            return ToNode(node);
        }

        /// <summary>Deleting is deactivating: the node and its products stay; Limen stops showing them under it.</summary>
        public async Task<StorefrontCategoryNodeDTO?> SetActiveAsync(Guid id, bool active)
        {
            var node = await _context.StorefrontCategory.FirstOrDefaultAsync(n => n.Id == id);
            if (node == null)
                return null;

            node.IsActive = active;
            node.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();
            return ToNode(node);
        }

        /// <summary>
        /// Files the caller's products under a leaf. C4: every active row of a product's variant family moves
        /// with it. Answers how many rows changed.
        /// </summary>
        public async Task<int> AssignAsync(Guid enterpriseId, IReadOnlyCollection<Guid> productIds, Guid categoryId)
        {
            if (productIds.Count == 0)
                return 0;
            if (productIds.Count > MaxProductsPerAssignment)
                throw new ArgumentException($"No máximo {MaxProductsPerAssignment} produtos por vez.");

            await EnsureAssignableAsync(categoryId);

            var chosen = await _context.Product
                .Where(p => p.EnterpriseId == enterpriseId && productIds.Contains(p.Id))
                .Select(p => new { p.Id, p.VariantGroupId })
                .ToListAsync();
            var groups = chosen.Where(p => p.VariantGroupId != null).Select(p => p.VariantGroupId!.Value).Distinct().ToList();
            var ids = chosen.Select(p => p.Id).ToList();

            var rows = await _context.Product
                .Where(p => p.EnterpriseId == enterpriseId &&
                            (ids.Contains(p.Id) || (p.VariantGroupId != null && groups.Contains(p.VariantGroupId.Value))) &&
                            p.StorefrontCategoryId != categoryId)
                .ToListAsync();

            foreach (var row in rows)
            {
                row.StorefrontCategoryId = categoryId;
                row.UpdatedAt = DateTime.UtcNow;
            }

            await _context.SaveChangesAsync();
            return rows.Count;
        }

        public Task EnsureAssignableAsync(Guid categoryId) => ProductService.EnsureStorefrontCategoryAssignableAsync(_context, categoryId);

        private async Task EnsureCanHoldChildrenAsync(Guid parentId)
        {
            var parent = await _context.StorefrontCategory.AsNoTracking().FirstOrDefaultAsync(n => n.Id == parentId)
                ?? throw new ArgumentException("Categoria-mãe não encontrada.");
            var products = await _context.Product.IgnoreQueryFilters().CountAsync(p => p.StorefrontCategoryId == parentId && p.IsActive == true);
            if (products > 0)
                throw new ArgumentException($"\"{parent.Name}\" tem {products} produtos. Mova-os para outra categoria antes de criar subcategorias.");
        }

        private async Task<bool> IsDescendantAsync(Guid candidate, Guid of)
        {
            var parents = await _context.StorefrontCategory.AsNoTracking().ToDictionaryAsync(n => n.Id, n => n.ParentId);
            for (Guid? cursor = candidate; cursor is { } current; cursor = parents.GetValueOrDefault(current))
            {
                if (current == of)
                    return true;
            }
            return false;
        }

        private async Task<(string Name, string Slug, string? Description)> ValidateAsync(SaveStorefrontCategoryRequestDTO request, Guid? parentId, Guid? existingId)
        {
            var name = request.Name?.Trim() ?? string.Empty;
            if (name.Length is < 2 or > 60)
                throw new ArgumentException("O nome deve ter de 2 a 60 caracteres.");

            var slug = request.Slug?.Trim().ToLowerInvariant() ?? string.Empty;
            if (!SlugPattern().IsMatch(slug))
                throw new ArgumentException("O endereço deve ter de 3 a 60 letras minúsculas, números e hífens.");

            if (!Icons.Contains(request.Icon))
                throw new ArgumentException("Ícone desconhecido.");

            var description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim();
            if (description?.Length > 160)
                throw new ArgumentException("A descrição pode ter até 160 caracteres.");

            if (await _context.StorefrontCategory.AnyAsync(n => n.Slug == slug && n.Id != existingId) ||
                await _context.StorefrontCategorySlugAlias.AnyAsync(a => a.OldSlug == slug && a.CategoryId != existingId))
                throw new ArgumentException("Esse endereço já é usado por outra categoria.");

            if (await _context.StorefrontCategory.AnyAsync(n => n.ParentId == parentId && n.Name == name && n.Id != existingId))
                throw new ArgumentException("Já existe uma categoria com esse nome aqui.");

            return (name, slug, description);
        }

        private static StorefrontCategoryNodeDTO ToNode(StorefrontCategory node) =>
            new(node.Id, node.ParentId, node.Name, node.Slug, node.Icon, node.Description, node.SortOrder, node.IsActive == true, 0, 0, []);

        #endregion

        #region Limen (service account)

        /// <summary>Active nodes under active ancestors, with published card counts from listed shops.</summary>
        public async Task<IReadOnlyList<StorefrontPublicCategoryDTO>> GetPublicTreeAsync()
        {
            var nodes = await _context.StorefrontCategory.AsNoTracking().Where(n => n.IsActive == true).ToListAsync();
            var cards = await CardCountsAsync();

            StorefrontPublicCategoryDTO Build(StorefrontCategory node)
            {
                var children = nodes.Where(n => n.ParentId == node.Id).OrderBy(n => n.SortOrder).ThenBy(n => n.Name).Select(Build).ToList();
                return new StorefrontPublicCategoryDTO(node.Slug, node.Name, node.Icon, node.Description,
                    cards.GetValueOrDefault(node.Id) + children.Sum(c => c.ProductCount), children);
            }

            return nodes.Where(n => n.ParentId == null).OrderBy(n => n.SortOrder).ThenBy(n => n.Name).Select(Build).ToList();
        }

        /// <summary>The category page by slug; an old slug answers with where the category lives now.</summary>
        public async Task<StorefrontCategoryPageDTO?> GetPublicPageAsync(string slug)
        {
            var tree = await GetPublicTreeAsync();
            var redirect = (string?)null;
            var current = Find(tree, slug, []);
            if (current == null)
            {
                var alias = await _context.StorefrontCategorySlugAlias.AsNoTracking()
                    .Where(a => a.OldSlug == slug)
                    .Select(a => a.Category!.Slug)
                    .FirstOrDefaultAsync();
                if (alias == null)
                    return null;
                redirect = alias;
                current = Find(tree, alias, []);
                if (current == null)
                    return null;
            }

            return new StorefrontCategoryPageDTO(current.Value.Node, current.Value.Path, redirect);
        }

        /// <summary>The ids a category filter covers: the node and every active descendant. Empty when unknown or inactive.</summary>
        internal static async Task<IReadOnlyList<Guid>> CoveredIdsAsync(AppDbContext context, string slug)
        {
            var nodes = await context.StorefrontCategory.AsNoTracking().Where(n => n.IsActive == true)
                .Select(n => new { n.Id, n.ParentId, n.Slug }).ToListAsync();
            var root = nodes.FirstOrDefault(n => n.Slug == slug);
            if (root == null || !ActiveAncestors(root.ParentId))
                return [];

            var covered = new List<Guid> { root.Id };
            for (var i = 0; i < covered.Count; i++)
                covered.AddRange(nodes.Where(n => n.ParentId == covered[i]).Select(n => n.Id));
            return covered;

            bool ActiveAncestors(Guid? parentId) =>
                parentId is not { } id || (nodes.FirstOrDefault(n => n.Id == id) is { } parent && ActiveAncestors(parent.ParentId));
        }

        /// <summary>The breadcrumb of a category, department first; empty when it is unknown or inactive along the way.</summary>
        internal static async Task<IReadOnlyList<StorefrontCategoryPathItemDTO>> PathOfAsync(AppDbContext context, Guid categoryId)
        {
            var nodes = await context.StorefrontCategory.AsNoTracking().ToDictionaryAsync(n => n.Id);
            var path = new List<StorefrontCategoryPathItemDTO>();
            for (Guid? cursor = categoryId; cursor is { } id; cursor = nodes[id].ParentId)
            {
                if (!nodes.TryGetValue(id, out var node) || node.IsActive != true)
                    return [];
                path.Insert(0, new StorefrontCategoryPathItemDTO(node.Slug, node.Name));
            }
            return path;
        }

        private async Task<Dictionary<Guid, int>> CardCountsAsync() =>
            (await StorefrontService.Published(_context)
                .Where(p => p.StorefrontCategoryId != null)
                .Select(p => new { Category = p.StorefrontCategoryId!.Value, p.EnterpriseId, Key = p.VariantGroupId ?? p.Id })
                .Distinct()
                .ToListAsync())
            .GroupBy(x => x.Category)
            .ToDictionary(g => g.Key, g => g.Count());

        private static (StorefrontPublicCategoryDTO Node, IReadOnlyList<StorefrontCategoryPathItemDTO> Path)? Find(
            IReadOnlyList<StorefrontPublicCategoryDTO> level, string slug, IReadOnlyList<StorefrontCategoryPathItemDTO> path)
        {
            foreach (var node in level)
            {
                var here = path.Append(new StorefrontCategoryPathItemDTO(node.Slug, node.Name)).ToList();
                if (node.Slug == slug)
                    return (node, here);
                if (Find(node.Children, slug, here) is { } found)
                    return found;
            }
            return null;
        }

        #endregion

        [GeneratedRegex("^[a-z0-9](?:[a-z0-9-]{1,58})[a-z0-9]$", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
        private static partial Regex SlugPattern();
    }
}
