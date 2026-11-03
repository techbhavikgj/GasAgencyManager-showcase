namespace GasAgencyManager.Models
{
    public class User
    {
        public int Id { get; set; }
        public string Username { get; set; } = string.Empty;
        public UserRole Role { get; set; }
    }
}



