using System.Net;
using System.Net.Mail;
using System.Security.Cryptography;
using BankSlipScannerApp.Data;
using BankSlipScannerApp.DTOs;
using Microsoft.EntityFrameworkCore;

namespace BankSlipScannerApp.Services
{
    public interface IPasswordService
    {
        Task<AuthResultDto> ForgotPasswordAsync(ForgotPasswordDto request);
        Task<AuthResultDto> ResetPasswordAsync(ResetPasswordDto request);
    }

    public class PasswordService : IPasswordService
    {
        private readonly IConfiguration _config;
        private readonly AppDbContext _context;

        // Stockage des tokens en mémoire (clé = email, valeur = token + expiration)
        public static readonly Dictionary<string, (string Token, DateTime ExpiresAt)> _resetTokens = new();

        public PasswordService(IConfiguration config, AppDbContext context)
        {
            _config = config;
            _context = context;
        }

        public async Task<AuthResultDto> ForgotPasswordAsync(ForgotPasswordDto request)
        {
            // 1. Chercher l'utilisateur dans la base
            var user = await _context.Users
                .FirstOrDefaultAsync(u => u.Email.ToLower() == request.Email.Trim().ToLower());

            // 2. Réponse générique (sécurité)
            if (user == null)
                return new AuthResultDto { Success = true, Message = "Si cet email existe, vous recevrez un lien." };

            // 3. Générer token sécurisé
            string token = GenerateSecureToken();

            // 4. Stocker token avec expiration 1 heure
            _resetTokens[user.Email.ToLower()] = (token, DateTime.UtcNow.AddHours(1));

            // 5. Envoyer email
            await SendResetEmailAsync(user.Email, user.NomComplet, token);

            return new AuthResultDto { Success = true, Message = "Si cet email existe, vous recevrez un lien." };
        }

        public async Task<AuthResultDto> ResetPasswordAsync(ResetPasswordDto request)
        {
            string emailKey = request.Email.Trim().ToLower();

            // 1. Vérifier si token existe
            if (!_resetTokens.TryGetValue(emailKey, out var resetInfo))
                return new AuthResultDto { Success = false, Message = "Token invalide ou expiré." };

            // 2. Vérifier token
            if (resetInfo.Token != request.Token)
                return new AuthResultDto { Success = false, Message = "Token invalide ou expiré." };

            // 3. Vérifier expiration
            if (DateTime.UtcNow > resetInfo.ExpiresAt)
            {
                _resetTokens.Remove(emailKey);
                return new AuthResultDto { Success = false, Message = "Token expiré. Veuillez recommencer." };
            }

            // 4. Trouver utilisateur dans la base
            var user = await _context.Users
                .FirstOrDefaultAsync(u => u.Email.ToLower() == emailKey);

            if (user == null)
                return new AuthResultDto { Success = false, Message = "Utilisateur introuvable." };

            // 5. Mettre à jour le mot de passe dans la base
            user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.NewPassword, workFactor: 12);
            await _context.SaveChangesAsync();

            // 6. Supprimer le token utilisé
            _resetTokens.Remove(emailKey);

            return new AuthResultDto { Success = true, Message = "Mot de passe réinitialisé avec succès." };
        }

        private async Task SendResetEmailAsync(string toEmail, string nomComplet, string token)
        {
            var emailSettings = _config.GetSection("EmailSettings");
            string host = emailSettings["Host"] ?? "smtp.gmail.com";
            int port = int.Parse(emailSettings["Port"] ?? "587");
            string from = emailSettings["From"] ?? "";
            string password = emailSettings["Password"] ?? "";
            string baseUrl = emailSettings["BaseUrl"] ?? "http://localhost:5182";

            string resetLink = $"{baseUrl}/reset-password?token={token}&email={Uri.EscapeDataString(toEmail)}";

            string htmlBody = $@"
                <h2>Bonjour {nomComplet},</h2>
                <p>Cliquez sur ce lien pour réinitialiser votre mot de passe :</p>
                <a href='{resetLink}' style='background:#2563EB;color:white;padding:12px 24px;text-decoration:none;border-radius:8px;'>
                    Réinitialiser mon mot de passe →
                </a>
                <p>Ce lien expire dans <strong>1 heure</strong>.</p>";

            using var client = new SmtpClient(host, port)
            {
                Credentials = new NetworkCredential(from, password),
                EnableSsl = true
            };

            var mail = new MailMessage
            {
                From = new MailAddress(from, "Lynx ERP"),
                Subject = "Réinitialisation de votre mot de passe",
                Body = htmlBody,
                IsBodyHtml = true
            };
            mail.To.Add(toEmail);

            await client.SendMailAsync(mail);
        }

        private static string GenerateSecureToken()
        {
            var bytes = new byte[32];
            using var rng = RandomNumberGenerator.Create();
            rng.GetBytes(bytes);
            return Convert.ToHexString(bytes).ToLower();
        }
    }
}