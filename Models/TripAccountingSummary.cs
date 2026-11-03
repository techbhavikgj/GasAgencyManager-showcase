namespace GasAgencyManager.Models
{
    public class TripAccountingSummary
    {
        public int MorningTotal { get; set; }
        public int EmptiesReturned { get; set; }
        public int UnsoldFilledReturned { get; set; }
        public int RefillsSold => MorningTotal - UnsoldFilledReturned;
    }
}


