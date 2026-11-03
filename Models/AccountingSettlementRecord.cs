using System;

namespace GasAgencyManager.Models
{
    public class AccountingSettlementRecord
    {
        public int Id { get; set; }
        public int DriverId { get; set; }
        public int VehicleId { get; set; }
        public string SettlementDate { get; set; } = "";
        public DateTime SavedAt { get; set; }
        public string DriverName { get; set; } = "";
        public string VehicleNumber { get; set; } = "";
        public int ExpectedRefills { get; set; }
        public int RefillsEntered { get; set; }
        public decimal TotalCollection { get; set; }
        public string SummaryText { get; set; } = "";

        public string SavedAtDisplay => SavedAt.ToString("dd-MMM-yyyy HH:mm");
        public string SettlementDateDisplay => DateTime.TryParse(SettlementDate, out var d)
            ? d.ToString("dd-MMM-yyyy")
            : SettlementDate;
        public string TotalCollectionDisplay => $"₹{TotalCollection:F2}";
        public string RefillsDisplay => $"{RefillsEntered} / {ExpectedRefills}";
    }
}


