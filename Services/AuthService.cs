using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using BankSlipScannerApp.Data;
using BankSlipScannerApp.DTOs;
using BankSlipScannerApp.Models;

namespace BankSlipScannerApp.Services
{
    // INTERFACE
    public interface IAuthService
    {
        Task<AuthResultDto> LoginAsync(LoginDto request);
        Task<AuthResultDto> RegisterAsync(RegisterDto request);
    }

    // IMPLÉMENTATION
    public class AuthService : IAuthService
    {
        private readonly IConfiguration _config;
        private readonly AppDbContext _context; // ← Vraie base de données

        public AuthService(IConfiguration config, AppDbContext context)
        {
            _config = config;
            _context = context;
        }

        // ─── LOGIN ────────────────────────────────────────────────────────────
        public async Task<AuthResultDto> LoginAsync(LoginDto request)
        {
            // 1. Rechercher l'utilisateur dans la base SQL Server
            var user = await _context.Users
                .FirstOrDefaultAsync(u => u.Email.ToLower() == request.Email.Trim().ToLower());

            // 2. Message générique pour ne pas divulguer si l'email existe
            if (user == null)
                return new AuthResultDto { Success = false, Message = "Email ou mot de passe incorrect." };

            // 3. Vérifier le mot de passe avec BCrypt
            bool isValid = BCrypt.Net.BCrypt.Verify(request.Password, user.PasswordHash);
            if (!isValid)
                return new AuthResultDto { Success = false, Message = "Email ou mot de passe incorrect." };

            // 4. Générer le token JWT
            string token = GenerateJwtToken(user, request.RememberMe);

            return new AuthResultDto
            {
                Success = true,
                Message = "Connexion réussie.",
                Token = token,
                ExpiresIn = request.RememberMe ? "30 jours" : "1 jour",
                User = new UserDto
                {
                    Id = user.Id,
                    NomComplet = user.NomComplet,
                    Email = user.Email,
                    Role = user.Role
                }
            };
        }

        //  REGISTER 
        public async Task<AuthResultDto> RegisterAsync(RegisterDto request)
        {
            // 1. Vérifier si l'email existe déjà dans la base
            bool emailExists = await _context.Users
                .AnyAsync(u => u.Email.ToLower() == request.Email.Trim().ToLower());

            if (emailExists)
                return new AuthResultDto { Success = false, Message = "Cet email est déjà utilisé." };

            // 2. Générer un salt aléatoire
            string separateSalt = GenerateSalt();

            // 3. Hasher le mot de passe avec BCrypt
            string passwordHash = BCrypt.Net.BCrypt.HashPassword(request.Password, workFactor: 12);

            // 4. Créer le nouvel utilisateur
            var newUser = new User
            {
                NomComplet = request.NomComplet.Trim(),
                Email = request.Email.Trim().ToLower(),
                PasswordHash = passwordHash,
                PasswordSalt = separateSalt,
                Role = "user",
                CreatedAt = DateTime.UtcNow,
                IsActive = true
            };

            // 5. Sauvegarder dans la base SQL Server
            _context.Users.Add(newUser);
            await _context.SaveChangesAsync();

            return new AuthResultDto
            {
                Success = true,
                Message = "Compte créé avec succès."
            };
        }

        //  GÉNÉRATION DU TOKEN JWT
        private string GenerateJwtToken(User user, bool rememberMe)
        {
            var jwtSettings = _config.GetSection("JwtSettings");
            var secretKey = jwtSettings["SecretKey"]
                ?? throw new InvalidOperationException("JwtSettings:SecretKey manquant dans appsettings.json");

            var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secretKey));
            var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

            var claims = new[]
            {
                new Claim("userId",     user.Id.ToString()),
                new Claim("nomComplet", user.NomComplet),
                new Claim("email",      user.Email),
                new Claim("role",       user.Role),
                new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
                new Claim(JwtRegisteredClaimNames.Iat,
                    DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(),
                    ClaimValueTypes.Integer64)
            };

            var expiration = rememberMe
                ? DateTime.UtcNow.AddDays(30)
                : DateTime.UtcNow.AddDays(1);

            var token = new JwtSecurityToken(
                issuer: jwtSettings["Issuer"] ?? "lynx-erp",
                audience: jwtSettings["Audience"] ?? "lynx-erp-users",
                claims: claims,
                expires: expiration,
                signingCredentials: creds
            );

            return new JwtSecurityTokenHandler().WriteToken(token);
        }

        // ─── GÉNÉRATION D'UN SALT ALÉATOIRE ──────────────────────────────────
        private static string GenerateSalt()
        {
            var saltBytes = new byte[32];
            using var rng = RandomNumberGenerator.Create();
            rng.GetBytes(saltBytes);
            return Convert.ToBase64String(saltBytes);
        }

        // ─── ACCÈS PUBLIC À LA LISTE (pour PasswordService) ──────────────────
        public static List<User> GetUsers() => new(); // Plus utilisé avec SQL Server
    }
}