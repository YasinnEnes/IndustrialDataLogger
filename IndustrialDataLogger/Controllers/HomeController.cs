using Microsoft.AspNetCore.Mvc;
using System.IO;

namespace IndustrialDataLogger.Controllers
{
    [ApiController]
    [Route("")]
    public class HomeController : ControllerBase
    {
        // 1. Kök dizine gelindiğinde kesinlikle login.html sunulur
        [HttpGet]
        public IActionResult GetRoot()
        {
            var filePath = Path.Combine(Directory.GetCurrentDirectory(), "Dashboard", "login.html");
            return PhysicalFile(filePath, "text/html");
        }

        // 2. Kullanıcı direkt /index.html veya /control.html isteğinde bulunursa
        [HttpGet("{filename}")]
        public IActionResult GetFile(string filename)
        {
            // Eğer istenen dosya index.html ise, sunucu tarafında bir güvenlik kontrolü simüle edebiliriz.
            // Ancak tarayıcı localStorage'ı C# göremediği için en sağlam yol dosya adını kontrol etmektir.
            var filePath = Path.Combine(Directory.GetCurrentDirectory(), "Dashboard", filename);

            if (System.IO.File.Exists(filePath))
            {
                return PhysicalFile(filePath, "text/html");
            }
            return NotFound();
        }
    }
}