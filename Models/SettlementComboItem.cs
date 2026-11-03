namespace GasAgencyManager.Models
{
    public class DriverComboItem
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
        public bool IsSettled { get; set; }
        public string DisplayName => IsSettled ? $"{Name} — Settlement Done" : Name;
        public bool IsEnabled => !IsSettled;
    }

    public class VehicleComboItem
    {
        public int Id { get; set; }
        public string VehicleNumber { get; set; } = "";
        public bool IsSettled { get; set; }
        public string DisplayName => IsSettled ? $"{VehicleNumber} — Settlement Done" : VehicleNumber;
        public bool IsEnabled => !IsSettled;
    }
}


