namespace GasAgencyManager.Models
{
    public class StockEntry
    {
        public int WarehouseId { get; set; }
        public string CylinderType { get; set; } = string.Empty; // Fixed here
        public int Quantity { get; set; }
        public string EntryType { get; set; } = string.Empty;    // Fixed here
    }
}


