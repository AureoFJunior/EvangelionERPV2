using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace EvangelionERPV2.Shared.Migrations
{
    /// <inheritdoc />
    public partial class AddStorefrontCategories : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "StorefrontCategoryId",
                table: "Product",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "StorefrontCategory",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ParentId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Name = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    Slug = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    Icon = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(160)", maxLength: 160, nullable: true),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StorefrontCategory", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StorefrontCategory_StorefrontCategory_ParentId",
                        column: x => x.ParentId,
                        principalTable: "StorefrontCategory",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "StorefrontCategorySlugAlias",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OldSlug = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    CategoryId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StorefrontCategorySlugAlias", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StorefrontCategorySlugAlias_StorefrontCategory_CategoryId",
                        column: x => x.CategoryId,
                        principalTable: "StorefrontCategory",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                table: "StorefrontCategory",
                columns: new[] { "Id", "CreatedAt", "Description", "Icon", "IsActive", "Name", "ParentId", "Slug", "SortOrder", "UpdatedAt" },
                values: new object[,]
                {
                    { new Guid("1b1195cc-175b-3af1-00b8-06aa05ede75c"), new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, "CookingPot", true, "Casa e cozinha", null, "casa-e-cozinha", 0, new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { new Guid("20a3bdee-f0d4-9708-5554-625fe6303bcc"), new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, "Plant", true, "Jardim e plantas", null, "jardim-e-plantas", 10, new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { new Guid("476ec673-8c97-3fd9-e671-445e835e77c6"), new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, "Books", true, "Livros", null, "livros", 6, new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { new Guid("5b26d799-a7ea-8996-3400-e147380e2a9f"), new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, "PaintBrushHousehold", true, "Arte e artesanato", null, "arte-e-artesanato", 5, new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { new Guid("7e78a70c-bc6c-774f-11ee-5f8cedfd3152"), new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, "Barbell", true, "Esporte e lazer", null, "esporte-e-lazer", 7, new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { new Guid("93357353-50e6-e269-5230-021396d04bb3"), new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, "PawPrint", true, "Pet", null, "pet", 9, new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { new Guid("a43a733a-bd07-724f-3fc3-3df3364ec105"), new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, "Sparkle", true, "Beleza e cuidado", null, "beleza-e-cuidado", 4, new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { new Guid("a62b4820-5a25-1da5-f218-fde9ff5a3289"), new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, "Notebook", true, "Papelaria e escritório", null, "papelaria-e-escritorio", 2, new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { new Guid("b4ed28af-9dd3-d056-2445-47ef0cf6dbbe"), new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, "Baby", true, "Infantil", null, "infantil", 8, new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { new Guid("bb188a3f-c403-d4ec-91a1-2c576cf37a55"), new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, "TShirt", true, "Moda e acessórios", null, "moda-e-acessorios", 3, new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { new Guid("d27d0867-ed88-650b-0076-f22d1316e72a"), new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, "Coffee", true, "Alimentos e bebidas", null, "alimentos-e-bebidas", 1, new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { new Guid("e39bfc0d-13d3-f5ad-59c0-11a832119a06"), new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, "DeviceMobile", true, "Eletrônicos e acessórios", null, "eletronicos-e-acessorios", 11, new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { new Guid("1355cdf4-dc0a-0992-6db7-34339dc2644c"), new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, "Sunglasses", true, "Acessórios", new Guid("bb188a3f-c403-d4ec-91a1-2c576cf37a55"), "acessorios", 3, new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { new Guid("18ebf743-18f6-02cd-eede-50ef5848c1d6"), new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, "BeerBottle", true, "Bebidas", new Guid("d27d0867-ed88-650b-0076-f22d1316e72a"), "bebidas", 4, new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { new Guid("1f482814-a6bf-5007-b963-89553b20b828"), new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, "Flower", true, "Cosméticos naturais", new Guid("a43a733a-bd07-724f-3fc3-3df3364ec105"), "cosmeticos-naturais", 0, new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { new Guid("20461652-7cf1-7712-91b0-7b325639b39a"), new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, "TShirt", true, "Camisetas", new Guid("bb188a3f-c403-d4ec-91a1-2c576cf37a55"), "camisetas", 0, new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { new Guid("20aad1d8-d25b-786e-16e7-1c22de622bcd"), new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, "BowlFood", true, "Pratos e tigelas", new Guid("1b1195cc-175b-3af1-00b8-06aa05ede75c"), "pratos-e-tigelas", 1, new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { new Guid("21cd81e6-723d-ea51-c61b-3347316b6261"), new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, "CoffeeBean", true, "Café", new Guid("d27d0867-ed88-650b-0076-f22d1316e72a"), "cafe", 0, new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { new Guid("2d3e4951-f261-bbe7-6445-fdf1a54854f2"), new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, "Diamond", true, "Bijuterias", new Guid("bb188a3f-c403-d4ec-91a1-2c576cf37a55"), "bijuterias", 2, new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { new Guid("2fd16762-5785-7b6b-96c1-4ed6ea724744"), new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, "Lamp", true, "Decoração", new Guid("1b1195cc-175b-3af1-00b8-06aa05ede75c"), "decoracao", 4, new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { new Guid("30f43fa2-e7fb-f1b1-d4b3-920086f535c3"), new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, "CookingPot", true, "Panelas", new Guid("1b1195cc-175b-3af1-00b8-06aa05ede75c"), "panelas", 3, new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { new Guid("697e3950-9b6a-d718-d8b6-fbaaf733b7d7"), new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, "Barbell", true, "Fitness", new Guid("7e78a70c-bc6c-774f-11ee-5f8cedfd3152"), "fitness", 0, new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { new Guid("6df826d2-14d2-0938-0927-c5b3d64c0d84"), new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, "Dog", true, "Cães", new Guid("93357353-50e6-e269-5230-021396d04bb3"), "caes", 0, new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { new Guid("7c93056f-70c2-7ee2-33e2-6d6d6f2760d2"), new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, "Pencil", true, "Canetas e lápis", new Guid("a62b4820-5a25-1da5-f218-fde9ff5a3289"), "canetas-e-lapis", 1, new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { new Guid("7d19aa1e-2938-7b29-8add-5ab30c6938e6"), new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, "FrameCorners", true, "Quadros e gravuras", new Guid("5b26d799-a7ea-8996-3400-e147380e2a9f"), "quadros-e-gravuras", 1, new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { new Guid("7dc58c8e-1ef0-a4ec-3e8c-bb5f3c38afb9"), new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, "Newspaper", true, "Revistas e zines", new Guid("476ec673-8c97-3fd9-e671-445e835e77c6"), "revistas-e-zines", 1, new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { new Guid("834d0305-cb5f-e070-716e-b205d923f92a"), new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, "Coffee", true, "Canecas e xícaras", new Guid("1b1195cc-175b-3af1-00b8-06aa05ede75c"), "canecas-e-xicaras", 0, new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { new Guid("860a91d6-a4ca-33b8-0466-1e9cb14490b0"), new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, "Notebook", true, "Cadernos e agendas", new Guid("a62b4820-5a25-1da5-f218-fde9ff5a3289"), "cadernos-e-agendas", 0, new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { new Guid("93de3f62-2a67-bda4-633b-0e480d059390"), new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, "Plant", true, "Plantas", new Guid("20a3bdee-f0d4-9708-5554-625fe6303bcc"), "plantas", 0, new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { new Guid("9499f463-18d2-f9b1-b503-231ed24fd27c"), new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, "Backpack", true, "Bolsas e mochilas", new Guid("bb188a3f-c403-d4ec-91a1-2c576cf37a55"), "bolsas-e-mochilas", 1, new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { new Guid("9734dfa4-1131-7f59-ca7d-bdba7f40db14"), new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, "Palette", true, "Materiais", new Guid("5b26d799-a7ea-8996-3400-e147380e2a9f"), "materiais", 2, new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { new Guid("994a1533-72c7-c4b7-040b-95b858cc9960"), new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, "Folders", true, "Organização", new Guid("a62b4820-5a25-1da5-f218-fde9ff5a3289"), "organizacao", 3, new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { new Guid("9b4bdbdf-8032-39fb-f756-4f509ffa70b9"), new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, "Book", true, "Livros", new Guid("476ec673-8c97-3fd9-e671-445e835e77c6"), "livros-e-obras", 0, new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { new Guid("a1494b5e-c9ad-a9d7-56bc-54856f268148"), new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, "Cookie", true, "Doces e chocolates", new Guid("d27d0867-ed88-650b-0076-f22d1316e72a"), "doces-e-chocolates", 2, new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { new Guid("a304990b-7bd1-6375-2632-9fb0afd9c37b"), new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, "Leaf", true, "Chás", new Guid("d27d0867-ed88-650b-0076-f22d1316e72a"), "chas", 1, new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { new Guid("a8d30eed-46d2-f0a9-279d-8f29679d2f56"), new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, "PuzzlePiece", true, "Jogos e hobbies", new Guid("7e78a70c-bc6c-774f-11ee-5f8cedfd3152"), "jogos-e-hobbies", 1, new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { new Guid("ad958adc-93b6-a0f9-fb83-9623566940a8"), new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, "Pepper", true, "Empórios e temperos", new Guid("d27d0867-ed88-650b-0076-f22d1316e72a"), "emporios-e-temperos", 3, new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { new Guid("b2881aa2-17ae-d1fb-79b6-871ac37e491a"), new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, "Drop", true, "Sabonetes", new Guid("a43a733a-bd07-724f-3fc3-3df3364ec105"), "sabonetes", 1, new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { new Guid("bebb8d1a-71db-ee2a-e62f-d5e33a33ec4b"), new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, "Shovel", true, "Jardinagem", new Guid("20a3bdee-f0d4-9708-5554-625fe6303bcc"), "jardinagem", 2, new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { new Guid("c80fdc96-8860-9662-e184-9ec96292a865"), new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, "Headphones", true, "Áudio", new Guid("e39bfc0d-13d3-f5ad-59c0-11a832119a06"), "audio", 1, new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { new Guid("cd585d25-1bca-1462-5ec5-b13197b57537"), new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, "DeviceMobile", true, "Acessórios de celular", new Guid("e39bfc0d-13d3-f5ad-59c0-11a832119a06"), "acessorios-de-celular", 0, new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { new Guid("ce2aa1c5-db83-b754-8cdc-323547e7fd70"), new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, "Flower", true, "Vasos", new Guid("20a3bdee-f0d4-9708-5554-625fe6303bcc"), "vasos", 1, new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { new Guid("da1e7dd7-e12e-df60-9af6-d930a287409d"), new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, "Towel", true, "Têxteis", new Guid("1b1195cc-175b-3af1-00b8-06aa05ede75c"), "texteis", 5, new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { new Guid("e0daa23b-9fb2-76d7-b581-b109554e89c3"), new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, "Sparkle", true, "Perfumaria", new Guid("a43a733a-bd07-724f-3fc3-3df3364ec105"), "perfumaria", 2, new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { new Guid("e6b660c9-0cd3-4204-432c-679e2efa4615"), new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, "Cat", true, "Gatos", new Guid("93357353-50e6-e269-5230-021396d04bb3"), "gatos", 1, new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { new Guid("e747f721-7afb-d77b-994b-d3d60f3ec8fb"), new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, "Vase", true, "Cerâmica", new Guid("5b26d799-a7ea-8996-3400-e147380e2a9f"), "ceramica", 0, new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { new Guid("eaf18c55-7a98-91d8-3c41-058f1feb427c"), new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, "Cube", true, "Brinquedos", new Guid("b4ed28af-9dd3-d056-2445-47ef0cf6dbbe"), "brinquedos", 0, new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { new Guid("ec954f69-1d49-2c23-d098-81f80dd81059"), new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, "PaintBrush", true, "Arte e desenho", new Guid("a62b4820-5a25-1da5-f218-fde9ff5a3289"), "arte-e-desenho", 2, new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { new Guid("fab4419a-4269-cac8-53a9-e40e251aaae1"), new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, "Baby", true, "Bebês", new Guid("b4ed28af-9dd3-d056-2445-47ef0cf6dbbe"), "bebes", 1, new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Utc) },
                    { new Guid("fcd12496-ca2f-9afc-8c6d-bdf55145cecd"), new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Utc), null, "Knife", true, "Utensílios", new Guid("1b1195cc-175b-3af1-00b8-06aa05ede75c"), "utensilios", 2, new DateTime(2026, 10, 1, 0, 0, 0, 0, DateTimeKind.Utc) }
                });

            migrationBuilder.CreateIndex(
                name: "IX_Product_StorefrontCategoryId_IsPublishedOnStorefront",
                table: "Product",
                columns: new[] { "StorefrontCategoryId", "IsPublishedOnStorefront" });

            migrationBuilder.CreateIndex(
                name: "IX_StorefrontCategory_ParentId_SortOrder",
                table: "StorefrontCategory",
                columns: new[] { "ParentId", "SortOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_StorefrontCategory_Slug",
                table: "StorefrontCategory",
                column: "Slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_StorefrontCategorySlugAlias_CategoryId",
                table: "StorefrontCategorySlugAlias",
                column: "CategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_StorefrontCategorySlugAlias_OldSlug",
                table: "StorefrontCategorySlugAlias",
                column: "OldSlug",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Product_StorefrontCategory_StorefrontCategoryId",
                table: "Product",
                column: "StorefrontCategoryId",
                principalTable: "StorefrontCategory",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Product_StorefrontCategory_StorefrontCategoryId",
                table: "Product");

            migrationBuilder.DropTable(
                name: "StorefrontCategorySlugAlias");

            migrationBuilder.DropTable(
                name: "StorefrontCategory");

            migrationBuilder.DropIndex(
                name: "IX_Product_StorefrontCategoryId_IsPublishedOnStorefront",
                table: "Product");

            migrationBuilder.DropColumn(
                name: "StorefrontCategoryId",
                table: "Product");
        }
    }
}
