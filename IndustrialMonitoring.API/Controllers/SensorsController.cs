using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace IndustrialMonitoring.API.Controllers
{
    [Route("api/sensordata")]
    [ApiController]
    [Authorize] // Varsayılan olarak tüm endpoint'ler token (Authentication) gerektirir
    public class SensorDataController : ControllerBase
    {
        // 1. Viewer, Operator ve Admin okuyabilir (GET)
        [HttpGet]
        [Authorize(Roles = "Viewer,Operator,Admin")]
        public IActionResult GetSensorData()
        {
            return Ok(new { message = "Sensör verileri başarıyla listelendi." });
        }

        // 2. Sadece Operator ve Admin veri ekleyebilir (POST) - Viewer 403 alır
        [HttpPost]
        [Authorize(Roles = "Operator,Admin")]
        public IActionResult CreateSensorData([FromBody] object data)
        {
            return Ok(new { message = "Sensör verisi başarıyla eklendi." });
        }

        // 3. Sadece Admin silebilir (DELETE) - Viewer ve Operator 403 alır
        [HttpDelete("{id}")]
        [Authorize(Roles = "Admin")]
        public IActionResult DeleteSensorData(int id)
        {
            return Ok(new { message = $"{id} ID numaralı sensör verisi silindi." });
        }
    }
}