using System;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;

namespace IndustrialDataLogger.Services
{
    public interface IJwtTokenService
    {
        string GenerateToken(string username, string role);
    }

    public class JwtTokenService : IJwtTokenService
    {
        private readonly IConfiguration _configuration;

        public JwtTokenService(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        public string GenerateToken(string username, string role)
        {
            var secret = Environment.GetEnvironmentVariable("JWT_SECRET")
                ?? _configuration["JwtSettings:Secret"]
                ?? _configuration["JwtSettings:SecretKey"];

            if (string.IsNullOrWhiteSpace(secret) || secret.Contains("YOUR_SECURE_JWT_SECRET_KEY") || secret.Length < 32)
            {
                secret = "IndustrialDataLogger_Development_LocalSecretKey_2026_Min32Chars!";
            }

            var issuer = _configuration["JwtSettings:Issuer"] ?? "IndustrialDataLoggerAPI";
            var audience = _configuration["JwtSettings:Audience"] ?? "IndustrialDataLoggerDashboard";
            var expiryMinutes = int.TryParse(_configuration["JwtSettings:ExpiryMinutes"], out var exp) ? exp : 480;

            var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret));
            var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

            var claims = new[]
            {
                new Claim(JwtRegisteredClaimNames.Sub, username),
                new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
                new Claim(ClaimTypes.Name, username),
                new Claim(ClaimTypes.Role, role)
            };

            var token = new JwtSecurityToken(
                issuer: issuer,
                audience: audience,
                claims: claims,
                expires: DateTime.UtcNow.AddMinutes(expiryMinutes),
                signingCredentials: credentials);

            return new JwtSecurityTokenHandler().WriteToken(token);
        }
    }
}
