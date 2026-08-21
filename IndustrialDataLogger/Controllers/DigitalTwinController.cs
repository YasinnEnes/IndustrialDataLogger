using Microsoft.AspNetCore.Mvc;
using IndustrialDataLogger.Models.DTOs;
using IndustrialDataLogger.Services;
using System.Threading;
using System.Threading.Tasks;

namespace IndustrialDataLogger.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class DigitalTwinController : ControllerBase
    {
        private readonly IDigitalTwinService _digitalTwinService;

        public DigitalTwinController(IDigitalTwinService digitalTwinService)
        {
            _digitalTwinService = digitalTwinService;
        }

        /// <summary>
        /// Tüm dijital ikiz durumunu (sensörler, makine operasyonel durumu, sağlık skoru, alarmlar, KPI istatistikleri) tek bir modelde döner.
        /// </summary>
        [HttpGet("state")]
        public async Task<IActionResult> GetDigitalTwinState([FromQuery] int machineId = 1, CancellationToken cancellationToken = default)
        {
            var state = await _digitalTwinService.GetStateAsync(machineId, cancellationToken);
            return Ok(state);
        }

        /// <summary>
        /// Makinenin kural tabanlı sağlık skorunu ve 4 bileşenli (sıcaklık, basınç, bağlantı, alarm) puan kırılımını döner.
        /// </summary>
        [HttpGet("health")]
        public async Task<IActionResult> GetMachineHealth([FromQuery] int machineId = 1, CancellationToken cancellationToken = default)
        {
            var state = await _digitalTwinService.GetStateAsync(machineId, cancellationToken);
            return Ok(new
            {
                machineId = state.MachineId,
                machineCode = state.MachineCode,
                machineName = state.MachineName,
                operationalStatus = state.OperationalStatus.ToString(),
                healthScore = state.HealthScore,
                healthGrade = state.HealthGrade.ToString(),
                breakdown = state.HealthBreakdown,
                lastUpdate = state.LastUpdate
            });
        }

        /// <summary>
        /// Fabrika geneli tüm makinelerin durumunu ve aggregate KPI özetini döner.
        /// </summary>
        [HttpGet("plant-overview")]
        public async Task<IActionResult> GetPlantOverview(CancellationToken cancellationToken = default)
        {
            var overview = await _digitalTwinService.GetPlantOverviewAsync(cancellationToken);
            return Ok(overview);
        }
    }
}
