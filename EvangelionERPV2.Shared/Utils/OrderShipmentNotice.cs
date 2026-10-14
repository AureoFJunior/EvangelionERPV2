using System.Net;
using EvangelionERPV2.Shared.Entities;
using EvangelionERPV2.Shared.Enums;

namespace EvangelionERPV2.Shared.Utils
{
    /// <summary>When a customer is told their order shipped, and what the ERP e-mail says.</summary>
    public static class OrderShipmentNotice
    {
        /// <summary>Shipping an order, or correcting the tracking of one already shipped: once per change.</summary>
        public static bool ShouldNotify(Order before, int statusAfter, string? trackingAfter)
        {
            if (statusAfter != (int)EnumOrderStatus.Shipped)
                return false;

            return before.Status != (int)EnumOrderStatus.Shipped
                || !string.Equals(before.TrackingCode, trackingAfter, StringComparison.Ordinal);
        }

        /// <summary>The order number a store and its customer share outside Limen: "#" + the id's first 8 hex digits.</summary>
        public static string NumberFor(Guid orderId) => "#" + orderId.ToString("N")[..8].ToUpperInvariant();

        public static EmailStructure BuildEmail(Order order, Customer customer, string enterpriseName)
        {
            var store = string.IsNullOrWhiteSpace(enterpriseName) ? "A loja" : enterpriseName.Trim();
            var firstName = (customer.Name ?? string.Empty).Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
            var number = NumberFor(order.Id);

            var body = new System.Text.StringBuilder()
                .Append("<p>Olá").Append(firstName is null ? string.Empty : ", " + WebUtility.HtmlEncode(firstName)).Append("!</p>")
                .Append("<p>").Append(WebUtility.HtmlEncode(store)).Append(" enviou o seu pedido <strong>").Append(number).Append("</strong>.</p>");

            if (!string.IsNullOrWhiteSpace(order.TrackingCode))
                body.Append("<p>Código de rastreio: <strong>").Append(WebUtility.HtmlEncode(order.TrackingCode)).Append("</strong></p>");

            if (Uri.TryCreate(order.TrackingUrl, UriKind.Absolute, out var trackingUrl)
                && (trackingUrl.Scheme == Uri.UriSchemeHttps || trackingUrl.Scheme == Uri.UriSchemeHttp))
                body.Append("<p><a href=\"").Append(WebUtility.HtmlEncode(trackingUrl.AbsoluteUri)).Append("\">Acompanhar a entrega</a></p>");

            body.Append("<p>Obrigado pela compra.</p>");

            return new EmailStructure(body.ToString(), $"{store} enviou o seu pedido {number}", [customer.Email.Trim()]);
        }
    }
}
