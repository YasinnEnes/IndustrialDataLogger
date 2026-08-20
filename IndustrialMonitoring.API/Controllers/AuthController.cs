using IndustrialMonitoring.API.DTOs;
using IndustrialMonitoring.API.Services;
using Microsoft.AspNetCore.Mvc;

namespace IndustrialMonitoring.API.Controllers
{
    [Route("api/auth")]
    [ApiController]
    public class AuthController : ControllerBase
    {
        private readonly IAuthService _authService;

        public AuthController(IAuthService authService)
        {
            _authService = authService;
        }

        [HttpPost("register")]
        public async Task<IActionResult> Register([FromBody] RegisterRequestDto request)
        {
            var result = await _authService.RegisterAsync(request);
            if (!result)
                return BadRequest(new { message = "Bu kullanıcı adı veya e-posta adresi zaten kullanımda." });

            return StatusCode(201, new { message = "Kullanıcı başarıyla oluşturuldu." });
        }

        [HttpPost("login")]
        public async Task<ActionResult<LoginResponseDto>> Login([FromBody] LoginRequestDto request)
        {
            var response = await _authService.LoginAsync(request);
            if (response == null)
                return Unauthorized(new { message = "Geçersiz kullanıcı adı veya şifre." });

            return Ok(response);
        }
    }
}