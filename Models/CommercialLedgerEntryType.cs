namespace GasAgencyManager.Models
{
    public enum CommercialLedgerEntryType
    {
        /// <summary>19kg cylinder delivered on credit — increases money and empty-cylinder due.</summary>
        CreditSale19kg = 1,

        /// <summary>Customer paid against outstanding balance.</summary>
        PaymentReceived = 2,

        /// <summary>Customer returned empty 19kg cylinders.</summary>
        EmptyReturned = 3,

        /// <summary>Manual correction for money or cylinder balance.</summary>
        Adjustment = 4
    }
}


