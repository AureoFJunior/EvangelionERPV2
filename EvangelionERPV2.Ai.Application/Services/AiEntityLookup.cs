using EvangelionERPV2.AiModule.Application.Interface;
using EvangelionERPV2.Shared.Entities;
using EvangelionERPV2.Shared.Repositories;

namespace EvangelionERPV2.AiModule.Application.Services
{
    /// <summary>
    /// Turns whatever the model put in an id field into a real id. It sends the name about as often
    /// as the id, and truncates the id when it does send one, so a write that insisted on a whole
    /// GUID pushed the lookup onto the person at the keyboard. This does it instead.
    /// </summary>
    public static class AiEntityLookup
    {
        private const int Candidates = 5;

        // ponytail: an id fragment is matched by scanning one page of the tenant's rows in memory,
        // because SQL Server cannot index a prefix of a uniqueidentifier. Only runs for hex-shaped
        // input, which is rare. Raise or replace with a persisted short-id column if it ever matters.
        private const int PrefixScanCap = 500;

        private const int MinimumPrefix = 4;

        public static Task<AiLookupResult> ProductAsync(
            IRepository<Product> repository, string value, Guid enterpriseId) =>
            ResolveAsync(
                value,
                "produto",
                "buscar_produtos",
                async needle => (await AiQuery.OrEmptyAsync(() => repository.GetAllAsync(
                        x => x.EnterpriseId == enterpriseId
                             && x.IsActive == true
                             && (x.Name.Contains(needle) || (x.Sku != null && x.Sku.Contains(needle))))))
                    .Select(x => (x.Id, x.Name))
                    .ToList(),
                async () => (await AiQuery.OrEmptyPageAsync(() => repository.GetPagedAsync(
                        x => x.EnterpriseId == enterpriseId && x.IsActive == true,
                        query => query.OrderBy(x => x.Name),
                        1,
                        PrefixScanCap))).Items
                    .Select(x => (x.Id, x.Name))
                    .ToList(),
                async id => (await AiQuery.OrEmptyAsync(() => repository.GetAllAsync(
                        x => x.Id == id && x.EnterpriseId == enterpriseId && x.IsActive == true))).Count > 0);

        public static Task<AiLookupResult> CustomerAsync(
            IRepository<Customer> repository, string value, Guid enterpriseId) =>
            ResolveAsync(
                value,
                "cliente",
                "buscar_clientes",
                async needle => (await AiQuery.OrEmptyAsync(() => repository.GetAllAsync(
                        x => x.EnterpriseId == enterpriseId
                             && x.IsActive == true
                             && (x.Name.Contains(needle) || (x.Document != null && x.Document.Contains(needle))))))
                    .Select(x => (x.Id, x.Name))
                    .ToList(),
                async () => (await AiQuery.OrEmptyPageAsync(() => repository.GetPagedAsync(
                        x => x.EnterpriseId == enterpriseId && x.IsActive == true,
                        query => query.OrderBy(x => x.Name),
                        1,
                        PrefixScanCap))).Items
                    .Select(x => (x.Id, x.Name))
                    .ToList(),
                async id => (await AiQuery.OrEmptyAsync(() => repository.GetAllAsync(
                        x => x.Id == id && x.EnterpriseId == enterpriseId && x.IsActive == true))).Count > 0);

        private static bool LooksLikeIdFragment(string text) =>
            text.Length >= MinimumPrefix
            && text.All(character => Uri.IsHexDigit(character) || character == '-');

        private static async Task<AiLookupResult> ResolveAsync(
            string value,
            string noun,
            string searchTool,
            Func<string, Task<List<(Guid Id, string Name)>>> search,
            Func<Task<List<(Guid Id, string Name)>>> page,
            Func<Guid, Task<bool>> exists)
        {
            var text = (value ?? string.Empty).Trim();

            if (text.Length == 0)
                return AiLookupResult.Failed($"informe o {noun}: o id completo ou o nome.");

            if (Guid.TryParse(text, out var id) && id != Guid.Empty)
            {
                return await exists(id)
                    ? AiLookupResult.Found(id)
                    : AiLookupResult.Failed($"nenhum {noun} ativo com esse id. Use {searchTool} para achar o correto.");
            }

            if (LooksLikeIdFragment(text))
            {
                var byPrefix = (await page())
                    .Where(row => row.Id.ToString().StartsWith(text, StringComparison.OrdinalIgnoreCase))
                    .ToList();

                if (byPrefix.Count == 1)
                    return AiLookupResult.Found(byPrefix[0].Id);

                if (byPrefix.Count > 1)
                {
                    return AiLookupResult.Failed(
                        $"mais de um {noun} começa com \"{text}\": "
                        + string.Join("; ", byPrefix.Take(Candidates).Select(match => $"{match.Name} ({match.Id})"))
                        + ". Pergunte qual antes de continuar.");
                }
            }

            var matches = await search(text);

            var exact = matches
                .Where(match => string.Equals(match.Name.Trim(), text, StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (exact.Count == 1)
                return AiLookupResult.Found(exact[0].Id);

            var candidates = exact.Count > 1 ? exact : matches;

            return candidates.Count switch
            {
                0 => AiLookupResult.Failed(
                    $"nenhum {noun} encontrado com \"{text}\". Chame {searchTool} pelo nome e repita esta ação "
                    + "com o resultado — não peça o id ao usuário."),
                1 => AiLookupResult.Found(candidates[0].Id),
                _ => AiLookupResult.Failed(
                    $"mais de um {noun} corresponde a \"{text}\": "
                    + string.Join("; ", candidates.Take(Candidates).Select(match => $"{match.Name} ({match.Id})"))
                    + ". Pergunte qual antes de continuar."),
            };
        }
    }

    public sealed record AiLookupResult(Guid? Id, string? Error)
    {
        public static AiLookupResult Found(Guid id) => new(id, null);

        public static AiLookupResult Failed(string error) => new(null, error);
    }
}
