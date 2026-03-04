namespace BankSlipScannerApp.Models
{
    public class User
    {
        public int Id { get; set; }
        public string NomComplet { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string PasswordHash { get; set; } = string.Empty;
        public string PasswordSalt { get; set; } = string.Empty;
        public string Role { get; set; } = "user";
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public bool IsActive { get; set; } = true;
    }
}