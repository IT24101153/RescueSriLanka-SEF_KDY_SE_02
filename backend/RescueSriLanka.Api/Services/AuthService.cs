using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using RescueSriLanka.Api.Data;
using RescueSriLanka.Api.DTOs.Auth;
using RescueSriLanka.Api.Models;

namespace RescueSriLanka.Api.Services
{
    public interface IAuthService
    {
        Task<AuthResponse?> LoginAsync(LoginRequest request, CancellationToken cancellationToken);
        Task<AuthResponse?> RegisterCitizenAsync(RegisterRequest request, CancellationToken cancellationToken);
        Task<User?> FindByIdAsync(Guid id, CancellationToken cancellationToken);
    }

    public class AuthService : IAuthService
    {
        private readonly ApplicationDbContext _db;
        private readonly IConfiguration _config;
        private readonly PasswordHasher<User> _hasher = new();

        public AuthService(ApplicationDbContext db, IConfiguration config)
        {
            _db = db;
            _config = config;
        }

        public async Task<AuthResponse?> LoginAsync(LoginRequest request, CancellationToken cancellationToken)
        {
            var email = request.Email.Trim().ToLowerInvariant();
            var user = await _db.Users.FirstOrDefaultAsync(u => u.Email == email, cancellationToken);
            if (user is null || !user.IsActive) return null;

            var verifyResult = _hasher.VerifyHashedPassword(user, user.PasswordHash, request.Password);
            if (verifyResult == PasswordVerificationResult.Failed) return null;

            user.LastLoginAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(cancellationToken);

            return BuildAuthResponse(user);
        }

        public async Task<AuthResponse?> RegisterCitizenAsync(RegisterRequest request, CancellationToken cancellationToken)
        {
            var email = request.Email.Trim().ToLowerInvariant();
            var exists = await _db.Users.AnyAsync(u => u.Email == email, cancellationToken);
            if (exists) return null;

            var user = new User
            {
                FullName = request.FullName,
                Email = email,
                PasswordHash = string.Empty,
                Role = UserRole.Citizen,
                PhoneNumber = request.PhoneNumber
            };
            user.PasswordHash = _hasher.HashPassword(user, request.Password);

            _db.Users.Add(user);
            await _db.SaveChangesAsync(cancellationToken);

            return BuildAuthResponse(user);
        }

        public async Task<User?> FindByIdAsync(Guid id, CancellationToken cancellationToken) =>
            await _db.Users.FindAsync(new object[] { id }, cancellationToken);

        private AuthResponse BuildAuthResponse(User user)
        {
            var expiryMinutes = _config.GetValue<int?>("Jwt:ExpiryMinutes") ?? 480;
            var expiresAt = DateTime.UtcNow.AddMinutes(expiryMinutes);
            var token = GenerateJwt(user, expiresAt);
            return new AuthResponse
            {
                Token = token,
                ExpiresAt = expiresAt,
                User = UserDto.FromUser(user)
            };
        }

        private string GenerateJwt(User user, DateTime expiresAt)
        {
            var secret = _config["Jwt:Key"] ?? "local-dev-only-secret-change-before-merge-32chars-minimum";
            var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret));
            var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

            var claims = new[]
            {
                new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
                new Claim(ClaimTypes.Email, user.Email),
                new Claim(ClaimTypes.Role, user.Role.ToString()),
                new Claim(ClaimTypes.Name, user.FullName)
            };

            var token = new JwtSecurityToken(
                issuer: _config["Jwt:Issuer"] ?? "RescueSriLankaApi",
                audience: _config["Jwt:Audience"] ?? "RescueSriLankaClients",
                claims: claims,
                expires: expiresAt,
                signingCredentials: credentials);

            return new JwtSecurityTokenHandler().WriteToken(token);
        }
    }
}