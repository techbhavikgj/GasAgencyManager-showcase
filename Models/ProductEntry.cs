using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace GasAgencyManager.Models
{
    public class ProductEntry : INotifyPropertyChanged
    {
        private string _productName = "";
        private bool _isSelected;
        private int _quantity;
        private int _emptyQty;
        private int _returnQty;

        /// <summary>Morning uplift qty for this product (Evening In display only).</summary>
        private int _morningUpliftQty;

        public string ProductName
        {
            get => _productName;
            set
            {
                if (_productName != value)
                {
                    _productName = value;
                    OnPropertyChanged();
                }
            }
        }

        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (_isSelected != value)
                {
                    _isSelected = value;
                    OnPropertyChanged();
                }
            }
        }

        public int Quantity
        {
            get => _quantity;
            set
            {
                if (_quantity != value)
                {
                    _quantity = value;
                    OnPropertyChanged();
                }
            }
        }

        public int EmptyQty
        {
            get => _emptyQty;
            set
            {
                if (_emptyQty != value)
                {
                    _emptyQty = value;
                    OnPropertyChanged();
                }
            }
        }

        /// <summary>
        /// Quantity being returned to IOCL (empty or filled)
        /// </summary>
        public int ReturnQty
        {
            get => _returnQty;
            set
            {
                if (_returnQty != value)
                {
                    _returnQty = value;
                    OnPropertyChanged();
                }
            }
        }

        public int MorningUpliftQty
        {
            get => _morningUpliftQty;
            set
            {
                if (_morningUpliftQty != value)
                {
                    _morningUpliftQty = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(HasMorningUplift));
                    OnPropertyChanged(nameof(MorningUpliftLabel));
                }
            }
        }

        public bool HasMorningUplift => _morningUpliftQty > 0;

        public string MorningUpliftLabel => _morningUpliftQty > 0 ? $"Sent: {_morningUpliftQty}" : "";

        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string propertyName = "")
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}


