using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using IndustrialDataLogger.Enums;
using IndustrialDataLogger.Services;
using System;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;

namespace IndustrialDataLogger.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class AuthController : ControllerBase
    {
        private readonly IJwtTokenService _jwtTokenService;
        private readonly IEventLogService _eventLogService;

        public AuthController(IJwtTokenService jwtTokenService, IEventLogService eventLogService)
        {
            _jwtTokenService = jwtTokenService;
            _eventLogService = eventLogService;
        }

        [HttpPost("login")]
        public async Task<IActionResult> Login([FromBody] LoginRequest request, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(request.Username) || string.IsNullOrWhiteSpace(request.Password))
            {
                return BadRequest(new { success = false, message = "Kullanıcı adı ve şifre zorunludur." });
            }

            // Endüstriyel Rol Doğrulaması (RBAC)
            string role = "Operator";
            bool isAuthenticated = false;

            if (request.Username.Equals("admin", StringComparison.OrdinalIgnoreCase) && request.Password == "admin123")
            {
                role = "Admin";
                isAuthenticated = true;
            }
            else if (request.Username.Equals("programmer", StringComparison.OrdinalIgnoreCase) && request.Password == "prog123")
            {
                role = "Programmer";
                isAuthenticated = true;
            }
            else if (request.Username.Equals("operator", StringComparison.OrdinalIgnoreCase) && request.Password == "op123")
            {
                role = "Operator";
                isAuthenticated = true;
            }
            else if (request.Username.Equals("test", StringComparison.OrdinalIgnoreCase) && request.Password == "1234")
            {
                role = "Operator";
                isAuthenticated = true;
            }

            if (!isAuthenticated)
            {
                await _eventLogService.LogEventAsync("AUTH_FAILED", $"Başarısız giriş denemesi: {request.Username}", AlarmSeverity.Warning, "Security", cancellationToken);
                return Unauthorized(new { success = false, message = "Geçersiz kullanıcı adı veya şifre!" });
            }

            var token = _jwtTokenService.GenerateToken(request.Username, role);

            // Başarılı giriş sistem olayına kaydedilir (şifre loglanmaz!)
            await _eventLogService.LogEventAsync("USER_LOGIN", $"Kullanıcı sisteme giriş yaptı: {request.Username} (Rol: {role})", AlarmSeverity.Info, "Security", cancellationToken);

            return Ok(new
            {
                success = true,
                token = token,
                username = request.Username,
                role = role,
                expiresIn = 28800, // 8 saat
                message = "Giriş başarılı."
            });
        }

        [HttpGet("profile")]
        [Authorize]
        public IActionResult GetProfile()
        {
            var username = User.FindFirstValue(ClaimTypes.Name) ?? User.FindFirstValue(ClaimTypes.NameIdentifier);
            var role = User.FindFirstValue(ClaimTypes.Role);

            return Ok(new
            {
                username = username,
                role = role,
                isAuthenticated = true
            });
        }

        [HttpGet("verify")]
        public IActionResult Verify()
        {
            return Ok(new
            {
                status = "Active",
                authMethod = "JWT Bearer + RBAC",
                timestamp = DateTime.UtcNow
            });
        }
    }

    public class LoginRequest
    {
        public string Username { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
    }
}
