using System.Security.Cryptography;
using System.Text;
using EvangelionERPV2.Shared.Entities;

namespace EvangelionERPV2.Shared.Utils
{
    /// <summary>
    /// The starting tree (SPEC-limen-categories.md §2), seeded by the migration. Ids derive from the slug,
    /// so they are the same on every database. After seeding, the Limen team edits the tree in FrontV2.
    /// </summary>
    public static class StorefrontCategorySeed
    {
        private static readonly DateTime SeededAt = new(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc);

        private static readonly (string Name, string Slug, string Icon, (string Name, string Slug, string Icon)[] Children)[] Tree =
        [
            ("Casa e cozinha", "casa-e-cozinha", "CookingPot",
            [
                ("Canecas e xícaras", "canecas-e-xicaras", "Coffee"), ("Pratos e tigelas", "pratos-e-tigelas", "BowlFood"),
                ("Utensílios", "utensilios", "Knife"), ("Panelas", "panelas", "CookingPot"),
                ("Decoração", "decoracao", "Lamp"), ("Têxteis", "texteis", "Towel"),
            ]),
            ("Alimentos e bebidas", "alimentos-e-bebidas", "Coffee",
            [
                ("Café", "cafe", "CoffeeBean"), ("Chás", "chas", "Leaf"), ("Doces e chocolates", "doces-e-chocolates", "Cookie"),
                ("Empórios e temperos", "emporios-e-temperos", "Pepper"), ("Bebidas", "bebidas", "BeerBottle"),
            ]),
            ("Papelaria e escritório", "papelaria-e-escritorio", "Notebook",
            [
                ("Cadernos e agendas", "cadernos-e-agendas", "Notebook"), ("Canetas e lápis", "canetas-e-lapis", "Pencil"),
                ("Arte e desenho", "arte-e-desenho", "PaintBrush"), ("Organização", "organizacao", "Folders"),
            ]),
            ("Moda e acessórios", "moda-e-acessorios", "TShirt",
            [
                ("Camisetas", "camisetas", "TShirt"), ("Bolsas e mochilas", "bolsas-e-mochilas", "Backpack"),
                ("Bijuterias", "bijuterias", "Diamond"), ("Acessórios", "acessorios", "Sunglasses"),
            ]),
            ("Beleza e cuidado", "beleza-e-cuidado", "Sparkle",
            [
                ("Cosméticos naturais", "cosmeticos-naturais", "Flower"), ("Sabonetes", "sabonetes", "Drop"),
                ("Perfumaria", "perfumaria", "Sparkle"),
            ]),
            ("Arte e artesanato", "arte-e-artesanato", "PaintBrushHousehold",
            [
                ("Cerâmica", "ceramica", "Jar"), ("Quadros e gravuras", "quadros-e-gravuras", "FrameCorners"),
                ("Materiais", "materiais", "Palette"),
            ]),
            ("Livros", "livros", "Books",
            [
                ("Livros", "livros-e-obras", "Book"), ("Revistas e zines", "revistas-e-zines", "Newspaper"),
            ]),
            ("Esporte e lazer", "esporte-e-lazer", "Barbell",
            [
                ("Fitness", "fitness", "Barbell"), ("Jogos e hobbies", "jogos-e-hobbies", "PuzzlePiece"),
            ]),
            ("Infantil", "infantil", "Baby",
            [
                ("Brinquedos", "brinquedos", "Cube"), ("Bebês", "bebes", "Baby"),
            ]),
            ("Pet", "pet", "PawPrint",
            [
                ("Cães", "caes", "Dog"), ("Gatos", "gatos", "Cat"),
            ]),
            ("Jardim e plantas", "jardim-e-plantas", "Plant",
            [
                ("Plantas", "plantas", "Plant"), ("Vasos", "vasos", "Flower"), ("Jardinagem", "jardinagem", "Shovel"),
            ]),
            ("Eletrônicos e acessórios", "eletronicos-e-acessorios", "DeviceMobile",
            [
                ("Acessórios de celular", "acessorios-de-celular", "DeviceMobile"), ("Áudio", "audio", "Headphones"),
            ]),
        ];

        /// <summary>A stable id for a seeded slug (MD5 of a namespace + slug; not security relevant).</summary>
#pragma warning disable S4790 // Compatibility identifier only; changing it would orphan persisted category IDs.
        public static Guid IdOf(string slug) =>
            new(MD5.HashData(Encoding.UTF8.GetBytes("limen-category:" + slug)));
#pragma warning restore S4790

        public static IReadOnlyList<StorefrontCategory> Categories()
        {
            var nodes = new List<StorefrontCategory>();
            for (var d = 0; d < Tree.Length; d++)
            {
                var (name, slug, icon, children) = Tree[d];
                var departmentId = IdOf(slug);
                nodes.Add(Node(departmentId, null, name, slug, icon, d));
                for (var c = 0; c < children.Length; c++)
                {
                    var child = children[c];
                    nodes.Add(Node(IdOf(child.Slug), departmentId, child.Name, child.Slug, child.Icon, c));
                }
            }
            return nodes;
        }

        private static StorefrontCategory Node(Guid id, Guid? parentId, string name, string slug, string icon, int order) => new()
        {
            Id = id,
            ParentId = parentId,
            Name = name,
            Slug = slug,
            Icon = icon,
            SortOrder = order,
            CreatedAt = SeededAt,
            UpdatedAt = SeededAt,
            IsActive = true
        };
    }
}
