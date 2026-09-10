using EvangelionERPV2.AiModule.Application.Interface;
using EvangelionERPV2.AiModule.Application.Services;
using EvangelionERPV2.CustomerModule.Application.Interface;
using EvangelionERPV2.Shared.Entities;
using EvangelionERPV2.Shared.Repositories;
using Microsoft.Extensions.AI;
using System.Text.Json;

namespace EvangelionERPV2.AiModule.Application.Tools
{
    public sealed class AtualizarClienteTool : IAiTool
    {
        private readonly ICustomerService<Customer> _service;
        private readonly IRepository<Customer> _repository;

        public AtualizarClienteTool(ICustomerService<Customer> service, IRepository<Customer> repository)
        {
            _service = service;
            _repository = repository;
        }

        public string Name => "atualizar_cliente";

        public string Description =>
            "Altera dados de um cliente já cadastrado. Parâmetros: clienteId (o id devolvido por " +
            "buscar_clientes), e os campos a mudar: telefone, email, limiteCredito. " +
            "A alteração só acontece depois que a pessoa confirmar na tela.";

        public string RequiredPermission => "customers.update";

        public bool RequiresConfirmation => true;

        public string Summarize(IReadOnlyDictionary<string, object?> arguments) =>
            "Alterar o cliente " + AiArguments.Text(arguments, "clienteId")
            + AiArguments.Suffix(arguments, "telefone", ": telefone para ")
            + AiArguments.Suffix(arguments, "email", ", email para ")
            + AiArguments.Suffix(arguments, "limiteCredito", ", limite de crédito para R$ ");

        public string? Validate(IReadOnlyDictionary<string, object?> arguments) =>
            AiArguments.Id(arguments, "clienteId") is null
                ? "clienteId inválido: use o id completo devolvido por buscar_clientes, não o nome nem um pedaço do id."
                : null;

        public AIFunction Create(AiToolContext context) =>
            AIFunctionFactory.Create(async (string clienteId, string? telefone = null, string? email = null, decimal? limiteCredito = null) =>
            {
                if (!Guid.TryParse(clienteId, out var customerId) || customerId == Guid.Empty)
                    return "{\"erro\":\"clienteId inválido. Use buscar_clientes para obter o id.\"}";

                var customer = await AiQuery.ByIdOrNullAsync(() => _repository.GetByIdAsync(customerId));
                if (customer is null || customer.EnterpriseId != context.EnterpriseId)
                    return "{\"erro\":\"cliente não encontrado nesta empresa\"}";

                if (!string.IsNullOrWhiteSpace(telefone))
                    customer.PhoneNumber = telefone.Trim();

                if (!string.IsNullOrWhiteSpace(email))
                    customer.Email = email.Trim();

                if (limiteCredito is not null)
                    customer.CreditLimit = limiteCredito;

                var updated = _service.Update(customer);

                return JsonSerializer.Serialize(
                    new { id = updated.Id, nome = updated.Name, telefone = updated.PhoneNumber, limiteCredito = updated.CreditLimit },
                    AiJson.Options);
            }, Name, Description);
    }
}
