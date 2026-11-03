namespace GasAgencyManager.Models
{
    public class TransactionLog
    {
        public int Id { get; set; }
        public string WarehouseName { get; set; } = string.Empty;
        public string CylinderType { get; set; } = string.Empty;
        public int FilledChange { get; set; }
        public int EmptyChange { get; set; }
        public string TransactionType { get; set; } = string.Empty;
        public string Timestamp { get; set; } = string.Empty;
        public bool IsReversed { get; set; }
        public string ReversalReason { get; set; } = string.Empty;
    }
}


