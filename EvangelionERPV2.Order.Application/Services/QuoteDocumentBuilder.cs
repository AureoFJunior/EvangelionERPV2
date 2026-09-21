using EvangelionERPV2.Shared.Entities;
using EvangelionERPV2.Shared.Enums;
using System.Globalization;
using System.Net;
using System.Text;

namespace EvangelionERPV2.OrderModule.Application.Services
{
    /// <summary>
    /// A quote as a document a customer can be sent.
    /// </summary>
    public static class QuoteDocumentBuilder
    {
        private static readonly CultureInfo Culture = CultureInfo.GetCultureInfo("pt-BR");

        public static string BuildHtml(Quote quote, DateTime utcNow)
        {
            ArgumentNullException.ThrowIfNull(quote);

            var items = quote.Items?.OrderBy(item => item.Product?.Name ?? string.Empty).ToList() ?? [];

            var body = new StringBuilder();
            body.Append("<!DOCTYPE html><html lang='pt-BR'><head><meta charset='UTF-8'>");
            body.Append(StyleSheet);
            body.Append("</head><body>");
            body.Append(BuildHeader(quote, utcNow));
            body.Append(BuildParties(quote));
            body.Append(BuildItemsTable(items, quote.TotalValue));
            body.Append(BuildNotes(quote));
            body.Append(BuildFooter(quote));
            body.Append("</body></html>");

            return body.ToString();
        }

        private static string BuildHeader(Quote quote, DateTime utcNow)
        {
            var enterprise = quote.Enterprise;
            var status = quote.StatusAsOf(utcNow);

            var expiredBanner = status == EnumQuoteStatus.Expired
                ? $"<p class='expired'>Orçamento vencido em {Format(quote.ValidUntil)}.</p>"
                : string.Empty;

            return $"""
                <header>
                  <div class='issuer'>
                    <h1>{Encode(enterprise?.Name)}</h1>
                    <p>{Encode(enterprise?.Adress)}</p>
                    <p>{Encode(enterprise?.PhoneNumber)} {Encode(enterprise?.Email)}</p>
                  </div>
                  <div class='doc'>
                    <h2>Orçamento nº {quote.Number}</h2>
                    <p>Emissão: <strong>{Format(quote.IssuedAt)}</strong></p>
                    <p>Válido até: <strong>{Format(quote.ValidUntil)}</strong></p>
                  </div>
                </header>
                {expiredBanner}
                """;
        }

        private static string BuildParties(Quote quote)
        {
            var customer = quote.Customer;
            if (customer == null)
                return string.Empty;

            var document = string.IsNullOrWhiteSpace(customer.Document)
                ? string.Empty
                : $"<p>Documento: {Encode(customer.Document)}</p>";

            return $"""
                <section class='parties'>
                  <h3>Cliente</h3>
                  <p><strong>{Encode(customer.Name)}</strong></p>
                  {document}
                  <p>{Encode(customer.Adress)}</p>
                  <p>{Encode(customer.PhoneNumber)} {Encode(customer.Email)}</p>
                </section>
                """;
        }

        private static string BuildItemsTable(List<QuoteItem> items, decimal totalValue)
        {
            var rows = new StringBuilder();
            foreach (var item in items)
            {
                rows.Append($"""
                    <tr>
                      <td>{Encode(item.Product?.Name)}</td>
                      <td class='num'>{FormatQuantity(item.Quantity)} {Encode(item.UnitOfMeasure)}</td>
                      <td class='num'>{FormatMoney(item.UnitValue)}</td>
                      <td class='num'>{FormatMoney(item.LineAmount)}</td>
                    </tr>
                    """);
            }

            return $"""
                <table>
                  <thead>
                    <tr><th>Produto</th><th class='num'>Qtd.</th><th class='num'>Unitário</th><th class='num'>Total</th></tr>
                  </thead>
                  <tbody>{rows}</tbody>
                  <tfoot>
                    <tr><td colspan='3'>Total</td><td class='num'>{FormatMoney(totalValue)}</td></tr>
                  </tfoot>
                </table>
                """;
        }

        private static string BuildNotes(Quote quote)
        {
            return string.IsNullOrWhiteSpace(quote.Notes)
                ? string.Empty
                : $"<section class='notes'><h3>Observações</h3><p>{Encode(quote.Notes)}</p></section>";
        }

        private static string BuildFooter(Quote quote)
        {
            return $"""
                <footer>
                  <p>Este orçamento não reserva estoque e não constitui pedido.
                  Vale até {Format(quote.ValidUntil)}.</p>
                </footer>
                """;
        }

        /// <summary>
        /// Every value that reaches the page goes through here. The names and notes are typed by a
        /// user, and the renderer would treat unescaped markup as markup.
        /// </summary>
        private static string Encode(string? value) => WebUtility.HtmlEncode(value ?? string.Empty);

        private static string Format(DateTime value) => value.ToString("dd/MM/yyyy", Culture);

        private static string FormatMoney(decimal value) => value.ToString("N2", Culture);

        /// <summary>Trims a whole quantity to "3" while leaving 2.5 kg as "2,5".</summary>
        private static string FormatQuantity(decimal value) =>
            value == decimal.Truncate(value) ? value.ToString("N0", Culture) : value.ToString("N3", Culture);

        private const string StyleSheet = """
            <style>
              @page { size: A4; margin: 1.5cm; }
              body { font-family: Arial, Helvetica, sans-serif; font-size: 12px; color: #222; }
              header { display: flex; justify-content: space-between; border-bottom: 2px solid #222; padding-bottom: 8px; }
              header h1 { font-size: 18px; margin: 0 0 4px 0; }
              header h2 { font-size: 15px; margin: 0 0 4px 0; }
              header p { margin: 2px 0; }
              .doc { text-align: right; }
              .expired { margin: 10px 0 0 0; padding: 6px 8px; border: 1px solid #a00; color: #a00; font-weight: bold; }
              .parties { margin-top: 16px; }
              .parties h3, .notes h3 { font-size: 13px; margin: 0 0 4px 0; }
              .parties p { margin: 2px 0; }
              table { width: 100%; border-collapse: collapse; margin-top: 16px; }
              th, td { border-bottom: 1px solid #ddd; padding: 6px 4px; text-align: left; }
              th { background: #f2f2f2; }
              .num { text-align: right; }
              tfoot td { font-weight: bold; border-top: 2px solid #222; border-bottom: none; }
              .notes { margin-top: 16px; }
              footer { margin-top: 24px; font-size: 10px; color: #666; border-top: 1px solid #ddd; padding-top: 6px; }
            </style>
            """;
    }
}
