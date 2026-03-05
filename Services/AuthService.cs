using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.IdentityModel.Tokens;
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

        // Simulation d'une base de données en mémoire
       
        private static readonly List<User> _users = new()
        {
            new User
            {
                Id = 1,
                NomComplet = "Administrateur",
                Email = "admin@lynx-erp.com",
                // Mot de passe : "Admin1234!" hashé avec BCrypt
                PasswordHash = BCrypt.Net.BCrypt.HashPassword("Admin1234!"),
                Role = "admin"
            }
        };

        public AuthService(IConfiguration config)
        {
            _config = config;
        }

        // ─── LOGIN ────────────────────────────────────────────────────────────
        public async Task<AuthResultDto> LoginAsync(LoginDto request)
        {
            await Task.CompletedTask;

            // 1. Rechercher l'utilisateur par email (insensible à la casse)
            var user = _users.FirstOrDefault(u =>
                u.Email.Equals(request.Email.Trim(), StringComparison.OrdinalIgnoreCase));

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
            await Task.CompletedTask;

            // Vérifier si l'email existe déjà
            bool emailExists = _users.Any(u =>
                u.Email.Equals(request.Email.Trim(), StringComparison.OrdinalIgnoreCase));

            if (emailExists)
                return new AuthResultDto { Success = false, Message = "Cet email est déjà utilisé." };

            // Générer un salt aléatoire séparé 
            string separateSalt = GenerateSalt();

            // Hasher le mot de passe avec BCrypt 
            string passwordHash = BCrypt.Net.BCrypt.HashPassword(request.Password, workFactor: 12);

            var newUser = new User
            {
                Id = _users.Count + 1,
                NomComplet = request.NomComplet.Trim(),
                Email = request.Email.Trim().ToLower(),
                PasswordHash = passwordHash,
                PasswordSalt = separateSalt,
                Role = "user",
                CreatedAt = DateTime.UtcNow
            };

            _users.Add(newUser);

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
                new Claim("userId",    user.Id.ToString()),
                new Claim("nomComplet",user.NomComplet),
                new Claim("email",     user.Email),
                new Claim("role",      user.Role),
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

        //  un  SALT ALÉATOIRE (32 bytes) 
        private static string GenerateSalt()
        {
            var saltBytes = new byte[32];
            using var rng = RandomNumberGenerator.Create();
            rng.GetBytes(saltBytes);
            return Convert.ToBase64String(saltBytes);
        }
    }
}