namespace GasAgencyManager.Models
{
    public class CommercialCustomerSummary
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
        public string? ConsumerId { get; set; }
        public string? Phone { get; set; }
        public decimal AmountDue { get; set; }
        public int EmptyCylindersDue { get; set; }
        public DateTime? LastTransactionAt { get; set; }
    }
}


