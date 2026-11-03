namespace GasAgencyManager.Models
{
    public class CommercialCustomerImportResult
    {
        public int Added { get; set; }
        public int Updated { get; set; }
        public int Skipped { get; set; }
        public int OpeningBalancesPosted { get; set; }
        public List<string> Errors { get; } = new();
        public int TotalRead { get; set; }

        public bool HasErrors => Errors.Count > 0;
    }
}


