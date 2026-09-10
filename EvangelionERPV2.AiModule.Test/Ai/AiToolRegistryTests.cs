using EvangelionERPV2.AiModule.Application.Interface;
using EvangelionERPV2.AiModule.Application.Services;
using Microsoft.Extensions.AI;

namespace EvangelionERPV2.AiModule.Test.Ai
{
    /// <summary>
    /// The registry is the security boundary, not a convenience. A tool the caller lacks the
    /// permission for is never put in front of the model at all, so there is no prompt that can
    /// talk it into calling one: as far as that turn is concerned the tool does not exist.
    /// </summary>
    public class AiToolRegistryTests
    {
        private static readonly AiToolContext Context = new(Guid.NewGuid(), Guid.NewGuid());

        [Fact]
        public void Build_IncludesAToolTheCallerIsAllowed()
        {
            var registry = new AiToolRegistry(new[] { Tool("buscar_produtos", "products.read") });

            var tools = registry.Build(new HashSet<string> { "products.read" }, Context);

            Assert.Single(tools);
            Assert.Equal("buscar_produtos", tools[0].Name);
        }

        [Fact]
        public void Build_HidesAToolTheCallerIsNotAllowed()
        {
            var registry = new AiToolRegistry(new[]
            {
                Tool("buscar_produtos", "products.read"),
                Tool("previsao_de_caixa", "cashflow.forecast.read"),
            });

            // The caller holds only products.read; the forecast tool requires cashflow.forecast.read,
            // so the registry hides it — as far as the model is concerned, that tool does not exist.
            var tools = registry.Build(new HashSet<string> { "products.read" }, Context);

            Assert.Single(tools);
            Assert.DoesNotContain(tools, t => t.Name == "previsao_de_caixa");
        }

        [Fact]
        public void Build_WithNoPermissions_ReturnsNothing()
        {
            var registry = new AiToolRegistry(new[] { Tool("buscar_produtos", "products.read") });

            var tools = registry.Build(new HashSet<string>(), Context);

            Assert.Empty(tools);
        }

        [Fact]
        public void Find_ReturnsTheToolByItsModelFacingName()
        {
            var registry = new AiToolRegistry(new[] { Tool("buscar_produtos", "products.read") });

            Assert.NotNull(registry.Find("buscar_produtos"));
            Assert.Null(registry.Find("nao_existe"));
        }

        private static IAiTool Tool(string name, string permission) => new FakeTool(name, permission);

        private sealed class FakeTool : IAiTool
        {
            public FakeTool(string name, string permission)
            {
                Name = name;
                RequiredPermission = permission;
            }

            public string Name { get; }
            public string Description => "ferramenta de teste";
            public string RequiredPermission { get; }
            public bool RequiresConfirmation => false;

            public AIFunction Create(AiToolContext context) =>
                AIFunctionFactory.Create(() => "ok", Name, Description);
        }
    }
}
