namespace GasAgencyManager.Models
{
    public class StockItem
    {
        public int WarehouseId { get; set; }
        public string CylinderType { get; set; } = string.Empty; // Fixed here
        public int FilledQty { get; set; }
        public int EmptyQty { get; set; }
    }
}

