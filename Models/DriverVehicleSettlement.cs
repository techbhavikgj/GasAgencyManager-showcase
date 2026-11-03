using System;

namespace GasAgencyManager.Models
{
    public class DriverVehicleSettlement
    {
        public int Id { get; set; }
        public int WarehouseId { get; set; }
        public int DriverId { get; set; }
        public int VehicleId { get; set; }
        public string BusinessDate { get; set; } = "";
        public DateTime CompletedAt { get; set; }
        public string DriverName { get; set; } = "";
        public string VehicleNumber { get; set; } = "";
    }
}


