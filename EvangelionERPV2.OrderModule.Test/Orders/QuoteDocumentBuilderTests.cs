using EvangelionERPV2.OrderModule.Application.Services;
using EvangelionERPV2.Shared.Entities;
using EvangelionERPV2.Shared.Enums;

namespace EvangelionERPV2.OrderModule.Test
{
    /// <summary>
    /// The quote as a document. Only the HTML is exercised here — the renderer behind it is a
    /// headless browser, and it is already covered where it lives.
    /// </summary>
    public class QuoteDocumentBuilderTests
    {
        private static readonly DateTime Now = new(2026, 8, 28, 12, 0, 0, DateTimeKind.Utc);

        [Fact]
        public void BuildHtml_StatesTheNumberThePartiesAndTheStoredTotal()
        {
            var html = QuoteDocumentBuilder.BuildHtml(BuildQuote(), Now);

            Assert.Contains("Orçamento nº 42", html);
            Assert.Contains("Nerv Supplies LTDA", html);
            Assert.Contains("Shinji Ikari", html);
            Assert.Contains("Plugsuit", html);

            // The stored TotalValue, not a re-sum of the lines: it is what the customer was quoted.
            Assert.Contains("1.234,50", html);
        }

        [Fact]
        public void BuildHtml_EscapesEverythingAUserTyped()
        {
            var quote = BuildQuote();
            quote.Customer!.Name = "<script>alert('x')</script>";
            quote.Notes = "5 < 6 & \"quoted\"";

            var html = QuoteDocumentBuilder.BuildHtml(quote, Now);

            Assert.DoesNotContain("<script>", html);
            Assert.Contains("&lt;script&gt;", html);
            Assert.Contains("5 &lt; 6 &amp;", html);
        }

        [Fact]
        public void BuildHtml_SaysSoOnItsFace_WhenTheQuoteHasLapsed()
        {
            var quote = BuildQuote();
            quote.ValidUntil = Now.AddDays(-1);

            var html = QuoteDocumentBuilder.BuildHtml(quote, Now);

            // Asking for the PDF of an expired quote is usually asking precisely because it lapsed.
            Assert.Contains("Orçamento vencido", html);
        }

        [Fact]
        public void BuildHtml_OfALiveQuote_CarriesNoExpiryBanner()
        {
            var html = QuoteDocumentBuilder.BuildHtml(BuildQuote(), Now);

            Assert.DoesNotContain("Orçamento vencido", html);
        }

        [Fact]
        public void BuildHtml_OfAQuoteWithNoCustomer_StillRenders()
        {
            // A quote can be drafted before anyone decides who it is for.
            var quote = BuildQuote();
            quote.Customer = null;
            quote.CustomerId = null;

            var html = QuoteDocumentBuilder.BuildHtml(quote, Now);

            Assert.Contains("Orçamento nº 42", html);
            Assert.DoesNotContain("<h3>Cliente</h3>", html);
        }

        private static Quote BuildQuote() => new()
        {
            Id = Guid.NewGuid(),
            Number = 42,
            Status = (int)EnumQuoteStatus.Sent,
            IssuedAt = Now.AddDays(-2),
            ValidUntil = Now.AddDays(13),
            TotalValue = 1234.50m,
            Notes = "Entrega em 5 dias.",
            Enterprise = new Enterprise
            {
                Name = "Nerv Supplies LTDA",
                Adress = "Rua Geofront, 1 - Tokyo-3",
                PhoneNumber = "11 4002-8922",
                Email = "vendas@nerv.example"
            },
            CustomerId = Guid.NewGuid(),
            Customer = new Customer
            {
                Name = "Shinji Ikari",
                Document = "12345678901",
                Adress = "Rua Misato, 3",
                PhoneNumber = "11 90000-0000",
                Email = "shinji@nerv.example"
            },
            Items =
            [
                new QuoteItem
                {
                    ProductId = Guid.NewGuid(),
                    Product = new Product { Name = "Plugsuit" },
                    Quantity = 3m,
                    UnitValue = 411.50m,
                    LineAmount = 1234.50m,
                    UnitOfMeasure = nameof(EnumUnitOfMeasure.Unit)
                }
            ]
        };
    }
}
