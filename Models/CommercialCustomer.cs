namespace GasAgencyManager.Models
{
    public class CommercialCustomer
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
        public string? Phone { get; set; }
        public string? Address { get; set; }
        public string? City { get; set; }
        public string? Area { get; set; }
        public string? Email { get; set; }
        public string? ConsumerId { get; set; }
        public string? ConsumerNo { get; set; }
        public string? DistributorCode { get; set; }
        public string? ConsumerType { get; set; }
        public string? ConsumerCategory { get; set; }
        public string? Product { get; set; }
        public string? LastRefillDate { get; set; }
        public string? Remarks { get; set; }
        public bool IsActive { get; set; } = true;
        public DateTime CreatedAt { get; set; }
    }
}


