using EvangelionERPV2.AiModule.Application.Interface;
using EvangelionERPV2.AiModule.Application.Services;
using EvangelionERPV2.CustomerModule.Application.Interface;
using EvangelionERPV2.Shared.Entities;
using Microsoft.Extensions.AI;
using System.Text.Json;

namespace EvangelionERPV2.AiModule.Application.Tools
{
    public sealed class CriarClienteTool : IAiTool
    {
        private readonly ICustomerService<Customer> _service;

        public CriarClienteTool(ICustomerService<Customer> service) => _service = service;

        public string Name => "criar_cliente";

        public string Description =>
            "Cadastra um novo cliente. Parâmetros: nome (obrigatório), telefone, email, documento " +
            "(CPF ou CNPJ). O cadastro só acontece depois que a pessoa confirmar na tela.";

        public string RequiredPermission => "customers.create";

        public bool RequiresConfirmation => true;

        public string Summarize(IReadOnlyDictionary<string, object?> arguments) =>
            $"Cadastrar o cliente {AiArguments.Text(arguments, "nome")}"
            + AiArguments.Suffix(arguments, "telefone", ", telefone ")
            + AiArguments.Suffix(arguments, "documento", ", documento ");

        public Task<AiToolArguments> PrepareAsync(
            IReadOnlyDictionary<string, object?> arguments,
            AiToolContext context,
            CancellationToken cancellation = default) =>
            Task.FromResult(AiArguments.Text(arguments, "nome").Length == 0
                ? AiToolArguments.Refused("informe o nome do cliente.")
                : AiToolArguments.Ok(arguments));

        public AIFunction Create(AiToolContext context) =>
            AIFunctionFactory.Create(async (string nome, string? telefone = null, string? email = null, string? documento = null) =>
            {
                var name = (nome ?? string.Empty).Trim();
                if (name.Length == 0)
                    return "{\"erro\":\"informe o nome do cliente\"}";

                var created = await _service.CreateAsync(new Customer
                {
                    Id = Guid.NewGuid(),
                    Name = name,
                    PhoneNumber = (telefone ?? string.Empty).Trim(),
                    Email = (email ?? string.Empty).Trim(),
                    Document = string.IsNullOrWhiteSpace(documento) ? null : documento.Trim(),
                    EnterpriseId = context.EnterpriseId,
                    IsActive = true,
                });

                return JsonSerializer.Serialize(new { id = created.Id, nome = created.Name }, AiJson.Options);
            }, Name, Description);
    }
}
