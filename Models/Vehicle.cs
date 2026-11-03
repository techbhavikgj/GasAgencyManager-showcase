namespace GasAgencyManager.Models
{
    public class Vehicle
    {
        public int Id { get; set; }
        public string? VehicleNumber { get; set; } // Added '?' to fix the warning
    }
}

