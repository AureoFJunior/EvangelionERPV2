using EvangelionERPV2.StorefrontModule.Application.DTOs;
using Microsoft.Extensions.Configuration;

namespace EvangelionERPV2.StorefrontModule.Application.Services
{
    /// <summary>
    /// Card installments (SPEC-limen-pricing-rules §3): 1× to 12×, interest paid by the shopper from 2×,
    /// each instalment at least the minimum. Rates come from <c>Storefront:Installments:MonthlyRates</c>
    /// (percent a month for 2×, 3×, …) until the payment provider supplies them (IN-2); with no rate for a
    /// count, that count is not offered — Limen never invents a rate (I4).
    /// </summary>
    public sealed class StorefrontInstallments
    {
        public const int MaxCount = 12;

        private readonly int _maxCount;
        private readonly decimal _minValue;
        private readonly decimal[] _monthlyRates;

        public StorefrontInstallments(IConfiguration configuration)
        {
            var section = configuration.GetSection("Storefront:Installments");
            _maxCount = Math.Clamp(section.GetValue("MaxCount", MaxCount), 1, MaxCount);
            _minValue = section.GetValue("MinValue", 10m);
            _monthlyRates = section.GetSection("MonthlyRates").Get<decimal[]>() ?? [];
        }

        /// <summary>Every count offered for <paramref name="cashTotal"/>, 1× first.</summary>
        public IReadOnlyList<StorefrontInstallmentDTO> Plans(decimal cashTotal)
        {
            if (cashTotal <= 0)
                return [];

            var plans = new List<StorefrontInstallmentDTO> { new(1, cashTotal, cashTotal, 0, 0) };
            for (var count = 2; count <= _maxCount && count - 2 < _monthlyRates.Length; count++)
            {
                var plan = Plan(cashTotal, count, _monthlyRates[count - 2]);
                if (plan.Value < _minValue)
                    break;
                plans.Add(plan);
            }
            return plans;
        }

        /// <summary>
        /// Price-table (Tabela Price) instalment, rounded to the cent; the total is what the card is charged.
        /// CET a.a. compounds the monthly rate (no fees are charged on top in test mode).
        /// </summary>
        public static StorefrontInstallmentDTO Plan(decimal cashTotal, int count, decimal monthlyRatePercent)
        {
            var i = (double)monthlyRatePercent / 100;
            var value = i == 0
                ? Math.Round(cashTotal / count, 2, MidpointRounding.AwayFromZero)
                : Math.Round((decimal)((double)cashTotal * i / (1 - Math.Pow(1 + i, -count))), 2, MidpointRounding.AwayFromZero);
            var cetAnnual = Math.Round((decimal)((Math.Pow(1 + i, 12) - 1) * 100), 2, MidpointRounding.AwayFromZero);
            return new StorefrontInstallmentDTO(count, value, value * count, monthlyRatePercent, cetAnnual);
        }
    }
}
