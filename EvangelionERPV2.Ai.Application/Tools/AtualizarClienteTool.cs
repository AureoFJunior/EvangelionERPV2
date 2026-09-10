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
            "buscar_clientes, ou simplesmente o nome do cliente — o sistema resolve), e os campos " +
            "a mudar: telefone, email, limiteCredito. " +
            "A alteração só acontece depois que a pessoa confirmar na tela.";

        public string RequiredPermission => "customers.update";

        public bool RequiresConfirmation => true;

        public string Summarize(IReadOnlyDictionary<string, object?> arguments) =>
            "Alterar o cliente " + AiArguments.Text(arguments, "clienteId")
            + AiArguments.Suffix(arguments, "telefone", ": telefone para ")
            + AiArguments.Suffix(arguments, "email", ", email para ")
            + AiArguments.Suffix(arguments, "limiteCredito", ", limite de crédito para R$ ");

        public async Task<AiToolArguments> PrepareAsync(
            IReadOnlyDictionary<string, object?> arguments,
            AiToolContext context,
            CancellationToken cancellation = default)
        {
            var nothingToChange = AiArguments.Text(arguments, "telefone").Length == 0
                                  && AiArguments.Text(arguments, "email").Length == 0
                                  && AiArguments.Number(arguments, "limiteCredito") is null;

            // An id on its own used to reach the tool, change nothing and still answer "Feito.".
            if (nothingToChange)
                return AiToolArguments.Refused("informe telefone, email ou limiteCredito.");

            var found = await AiEntityLookup.CustomerAsync(
                _repository, AiArguments.Text(arguments, "clienteId"), context.EnterpriseId);

            if (found.Error is not null)
                return AiToolArguments.Refused(found.Error);

            return AiToolArguments.Ok(AiArguments.With(arguments, "clienteId", found.Id!.Value.ToString()));
        }

        public AIFunction Create(AiToolContext context) =>
            AIFunctionFactory.Create(async (string clienteId, string? telefone = null, string? email = null, decimal? limiteCredito = null) =>
            {
                if (!Guid.TryParse(clienteId, out var customerId) || customerId == Guid.Empty)
                    return "{\"erro\":\"clienteId inválido. Use buscar_clientes para obter o id.\"}";

                var customer = await AiQuery.ByIdOrNullAsync(() => _repository.GetByIdAsync(customerId));
                if (customer is null || customer.EnterpriseId != context.EnterpriseId)
                    return "{\"erro\":\"cliente não encontrado nesta empresa\"}";

                var changed = false;

                if (!string.IsNullOrWhiteSpace(telefone) && customer.PhoneNumber != telefone.Trim())
                {
                    customer.PhoneNumber = telefone.Trim();
                    changed = true;
                }

                if (!string.IsNullOrWhiteSpace(email) && customer.Email != email.Trim())
                {
                    customer.Email = email.Trim();
                    changed = true;
                }

                if (limiteCredito is not null && customer.CreditLimit != limiteCredito)
                {
                    customer.CreditLimit = limiteCredito;
                    changed = true;
                }

                // The model tends to echo the values it just read alongside the one being changed.
                // Writing nothing and reporting success is how "Feito." came to mean nothing happened.
                if (!changed)
                    return "{\"erro\":\"nada para alterar: o cliente já está com esses valores\"}";

                var updated = _service.Update(customer);

                return JsonSerializer.Serialize(
                    new { id = updated.Id, nome = updated.Name, telefone = updated.PhoneNumber, limiteCredito = updated.CreditLimit },
                    AiJson.Options);
            }, Name, Description);
    }
}
