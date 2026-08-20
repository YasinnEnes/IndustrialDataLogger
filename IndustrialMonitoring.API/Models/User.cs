using System;

namespace IndustrialMonitoring.API.Models // Kendi namespace'inize göre düzenleyebilirsiniz
{
    public class User
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public string Username { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string PasswordHash { get; set; } = string.Empty;
        public string Role { get; set; } = "Viewer"; // Roller: Admin, Operator, Viewer
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}