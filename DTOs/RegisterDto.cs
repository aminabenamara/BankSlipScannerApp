using System.ComponentModel.DataAnnotations;

namespace BankSlipScannerApp.DTOs
{
    public class RegisterDto
    {
        [Required(ErrorMessage = "Le nom complet est obligatoire.")]
        [MinLength(3, ErrorMessage = "Le nom complet doit contenir au moins 3 caractères.")]
        public string NomComplet { get; set; } = string.Empty;

        [Required(ErrorMessage = "L'email est obligatoire.")]
        [EmailAddress(ErrorMessage = "Format d'email invalide. Exemple : nom@entreprise.com")]
        public string Email { get; set; } = string.Empty;

        [Required(ErrorMessage = "Le mot de passe est obligatoire.")]
        [MinLength(8, ErrorMessage = "Le mot de passe doit contenir au moins 8 caractères.")]
        [RegularExpression(@"^(?=.*[A-Z])(?=.*\d).+$",
            ErrorMessage = "Le mot de passe doit contenir au moins une majuscule et un chiffre.")]
        public string Password { get; set; } = string.Empty;

        [Required(ErrorMessage = "La confirmation du mot de passe est obligatoire.")]
        [Compare("Password", ErrorMessage = "Les mots de passe ne correspondent pas.")]
        public string ConfirmPassword { get; set; } = string.Empty;
    }

}