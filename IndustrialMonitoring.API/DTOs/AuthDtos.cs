using System;

namespace IndustrialMonitoring.API.DTOs
{
    // Kayıt olma (Register) isteği için gelen veriler
    public class RegisterRequestDto
    {
        public string Username { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
        public string Role { get; set; } = "Viewer"; // Güvenlik gereği varsayılan rol Viewer
    }

    // Giriş yapma (Login) isteği için gelen veriler
    public class LoginRequestDto
    {
        public string Username { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
    }

    // Başarılı giriş sonrası istemciye döneceğimiz yanıt (JWT Token içerir)
    public class LoginResponseDto
    {
        public string Token { get; set; } = string.Empty;
        public DateTime ExpiresAt { get; set; }
        public string Username { get; set; } = string.Empty;
        public string Role { get; set; } = string.Empty;
    }
}