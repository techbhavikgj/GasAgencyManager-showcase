using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace GasAgencyManager.Models
{
    public class AccountingEntry : INotifyPropertyChanged
    {
        private decimal _cash;
        private decimal _online;
        private decimal _gpay;
        private decimal _cashRefill;
        private decimal _onlineRefill;
        private decimal _gpayRefill;
        private decimal _totalRefillAmount;
        private decimal _change;
        private decimal _fuel;
        private decimal _other;

        public int CylindersSold { get; set; }
        public int RefillsSold { get; set; }

        public decimal Cash { get => _cash; set { _cash = value; OnPropertyChanged(); OnPropertyChanged(nameof(TotalCash)); } }
        public decimal Online { get => _online; set { _online = value; OnPropertyChanged(); OnPropertyChanged(nameof(TotalCash)); } }
        public decimal GPay { get => _gpay; set { _gpay = value; OnPropertyChanged(); OnPropertyChanged(nameof(TotalCash)); } }
        public decimal CashRefill { get => _cashRefill; set { _cashRefill = value; OnPropertyChanged(); OnPropertyChanged(nameof(TotalCash)); } }
        public decimal OnlineRefill { get => _onlineRefill; set { _onlineRefill = value; OnPropertyChanged(); OnPropertyChanged(nameof(TotalCash)); } }
        public decimal GPayRefill { get => _gpayRefill; set { _gpayRefill = value; OnPropertyChanged(); OnPropertyChanged(nameof(TotalCash)); } }
        public decimal TotalRefillAmount { get => _totalRefillAmount; set { _totalRefillAmount = value; OnPropertyChanged(); } }
        public decimal Change { get => _change; set { _change = value; OnPropertyChanged(); OnPropertyChanged(nameof(NetCash)); } }
        public decimal Fuel { get => _fuel; set { _fuel = value; OnPropertyChanged(); OnPropertyChanged(nameof(TotalExpense)); OnPropertyChanged(nameof(NetCash)); } }
        public decimal Other { get => _other; set { _other = value; OnPropertyChanged(); OnPropertyChanged(nameof(TotalExpense)); OnPropertyChanged(nameof(NetCash)); } }

        public decimal TotalCash => Cash + CashRefill;
        public decimal TotalOnline => Online + OnlineRefill;
        public decimal TotalGPay => GPay + GPayRefill;
        public decimal TotalExpense => Fuel + Other;
        public decimal NetCash => TotalCash + Change - TotalExpense;

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string propertyName = "")
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    public class CashNoteEntry : INotifyPropertyChanged
    {
        private int _count;
        private int _denomination;
        public int Count { get => _count; set { _count = value; OnPropertyChanged(); OnPropertyChanged(nameof(Value)); } }
        public int Denomination { get => _denomination; set { _denomination = value; OnPropertyChanged(); } }
        public int Value => Count * Denomination;
        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string propertyName = "")
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}


