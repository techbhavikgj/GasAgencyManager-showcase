namespace GasAgencyManager.Models
{
    public class CommercialLedgerEntry
    {
        public int Id { get; set; }
        public int CustomerId { get; set; }
        public string CustomerName { get; set; } = "";
        public CommercialLedgerEntryType EntryType { get; set; }
        public string EntryTypeDisplay { get; set; } = "";
        public decimal AmountChange { get; set; }
        public int EmptyCylinderChange { get; set; }
        public int Filled19kgChange { get; set; }
        public string? Notes { get; set; }
        public string? CreatedBy { get; set; }
        public DateTime Timestamp { get; set; }
    }
}


