namespace GasAgencyManager.Models
{
    /// <summary>
    /// One row from IOCL / Indane customer register export (CSV).
    /// </summary>
    public class CommercialCustomerRegisterRow
    {
        public string Name { get; set; } = "";
        public string? Phone { get; set; }
        public string? Address { get; set; }
        public string? City { get; set; }
        public string? Area { get; set; }
        public string? Email { get; set; }
        public string? ConsumerId { get; set; }
        public string? ConsumerNo { get; set; }
        public string? DistributorCode { get; set; }
        public string? DistributorName { get; set; }
        public string? ConsumerStatus { get; set; }
        public string? ConsumerSubStatus { get; set; }
        public string? ConsumerType { get; set; }
        public string? ConsumerCategory { get; set; }
        public string? Product { get; set; }
        public string? SvOrderNo { get; set; }
        public string? District { get; set; }
        public string? LastRefillDate { get; set; }
        public string? Remarks { get; set; }
        public decimal? OpeningPaymentDue { get; set; }
        public int? OpeningEmptyCylindersDue { get; set; }

        public bool LooksCommercial()
        {
            static bool Has(string? value, params string[] tokens)
            {
                if (string.IsNullOrWhiteSpace(value)) return false;
                var v = value.ToUpperInvariant();
                return tokens.Any(t => v.Contains(t, StringComparison.Ordinal));
            }

            return Has(ConsumerType, "COMMERCIAL", "NON DOMESTIC", "NDNE")
                || Has(ConsumerCategory, "COMMERCIAL", "19")
                || Has(Product, "19", "COMMERCIAL", "NON DOMESTIC");
        }
    }
}


