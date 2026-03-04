namespace BankSlipScannerApp.DTOs
{
    public class AuthResultDto
    {
        public bool Success { get; set; }
        public string Message { get; set; } = string.Empty;
        public string? Token { get; set; }
        public string? ExpiresIn { get; set; }
        public UserDto? User { get; set; }
    }

    public class UserDto
    {
        public int Id { get; set; }
        public string NomComplet { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string Role { get; set; } = string.Empty;
    }
}