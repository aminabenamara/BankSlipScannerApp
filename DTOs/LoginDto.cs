using System.ComponentModel.DataAnnotations;

namespace BankSlipScannerApp.DTOs
{
    public class LoginDto
    {
        [Required(ErrorMessage = "L'email est obligatoire.")]
        [EmailAddress(ErrorMessage = "Format d'email invalide. Exemple : user@domaine.com")]
        public string Email { get; set; } = string.Empty;

        [Required(ErrorMessage = "Le mot de passe est obligatoire.")]
        [MinLength(8, ErrorMessage = "Le mot de passe doit contenir au moins 8 caractères.")]
        public string Password { get; set; } = string.Empty;

        // "Se souvenir de moi pendant 30 jours"
        public bool RememberMe { get; set; } = false;
    }
}