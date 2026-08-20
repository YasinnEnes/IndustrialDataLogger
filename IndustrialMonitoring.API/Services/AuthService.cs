using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using IndustrialMonitoring.API.Data;
using IndustrialMonitoring.API.DTOs;
using IndustrialMonitoring.API.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

namespace IndustrialMonitoring.API.Services
{
    public interface IAuthService
    {
        Task<bool> RegisterAsync(RegisterRequestDto request);
        Task<LoginResponseDto?> LoginAsync(LoginRequestDto request);
    }

    public class AuthService : IAuthService
    {
        private readonly AppDbContext _context;
        private readonly IConfiguration _configuration;

        public AuthService(AppDbContext context, IConfiguration configuration)
        {
            _context = context;
            _configuration = configuration;
        }

        public async Task<bool> RegisterAsync(RegisterRequestDto request)
        {
            // Aynı username veya email ile daha önce kayıt olunmuş mu kontrol et
            if (await _context.Users.AnyAsync(u => u.Username == request.Username || u.Email == request.Email))
                return false;

            // Güvenlik Kuralı: Kullanıcının dışarıdan kendisini Admin yapmasını engelle
            string assignedRole = "Viewer";
            if (!string.IsNullOrEmpty(request.Role) && request.Role.Equals("Operator", StringComparison.OrdinalIgnoreCase))
            {
                assignedRole = "Operator";
            }

            // Şifreyi BCrypt ile güvenli bir şekilde hash'le (Asla plaintext saklanmaz)
            string passwordHash = BCrypt.Net.BCrypt.HashPassword(request.Password);

            var user = new User
            {
                Username = request.Username,
                Email = request.Email,
                PasswordHash = passwordHash,
                Role = assignedRole
            };

            _context.Users.Add(user);
            await _context.SaveChangesAsync();
            return true;
        }

        public async Task<LoginResponseDto?> LoginAsync(LoginRequestDto request)
        {
            // Kullanıcıyı veritabanında bul
            var user = await _context.Users
                .FirstOrDefaultAsync(u => u.Username == request.Username);

            // Kullanıcı yoksa veya şifre eşleşmiyorsa null döndür (401 tetiklenecek)
            if (user == null || !BCrypt.Net.BCrypt.Verify(request.Password, user.PasswordHash))
                return null;

            // JWT Token üretimi
            var tokenHandler = new JwtSecurityTokenHandler();
            var secretKey = _configuration["JwtSettings:SecretKey"] ?? throw new InvalidOperationException("JwtSettings:SecretKey is missing.");
            var key = Encoding.UTF8.GetBytes(secretKey);

            var durationMinutes = double.Parse(_configuration["JwtSettings:DurationInMinutes"] ?? "60");
            var expires = DateTime.UtcNow.AddMinutes(durationMinutes);

            var tokenDescriptor = new SecurityTokenDescriptor
            {
                Subject = new ClaimsIdentity(new[]
                {
                    new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
                    new Claim(ClaimTypes.Name, user.Username),
                    new Claim(ClaimTypes.Role, user.Role)
                }),
                Expires = expires,
                Issuer = _configuration["JwtSettings:Issuer"],
                Audience = _configuration["JwtSettings:Audience"],
                SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(key), SecurityAlgorithms.HmacSha256Signature)
            };

            var token = tokenHandler.CreateToken(tokenDescriptor);

            return new LoginResponseDto
            {
                Token = tokenHandler.WriteToken(token),
                ExpiresAt = expires,
                Username = user.Username,
                Role = user.Role
            };
        }
    }
}