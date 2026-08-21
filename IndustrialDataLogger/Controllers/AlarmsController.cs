using Microsoft.AspNetCore.Mvc;
using IndustrialDataLogger.Enums;
using IndustrialDataLogger.Services;
using System.Threading;
using System.Threading.Tasks;

namespace IndustrialDataLogger.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class AlarmsController : ControllerBase
    {
        private readonly IAlarmService _alarmService;

        public AlarmsController(IAlarmService alarmService)
        {
            _alarmService = alarmService;
        }

        /// <summary>
        /// Anlık olarak aktif durumda olan tüm alarmları listeler (isteğe bağlı makine filtresiyle).
        /// </summary>
        [HttpGet("active")]
        public async Task<IActionResult> GetActiveAlarms([FromQuery] int? machineId, CancellationToken cancellationToken)
        {
            var activeAlarms = await _alarmService.GetActiveAlarmsAsync(machineId, cancellationToken);
            return Ok(activeAlarms);
        }

        /// <summary>
        /// Geçmiş alarmları filtrelenmiş ve sayfalanmış olarak listeler.
        /// </summary>
        [HttpGet("history")]
        public async Task<IActionResult> GetAlarmHistory(
            [FromQuery] int limit = 50,
            [FromQuery] AlarmSeverity? severity = null,
            [FromQuery] AlarmStatus? status = null,
            [FromQuery] int? machineId = null,
            CancellationToken cancellationToken = default)
        {
            var history = await _alarmService.GetAlarmHistoryAsync(limit, severity, status, machineId, cancellationToken);
            return Ok(history);
        }

        /// <summary>
        /// Belirtilen alarmı operatör tarafından onaylanmış (Acknowledged) olarak işaretler.
        /// </summary>
        [HttpPost("{id}/acknowledge")]
        public async Task<IActionResult> AcknowledgeAlarm(long id, CancellationToken cancellationToken)
        {
            var success = await _alarmService.AcknowledgeAlarmAsync(id, cancellationToken);
            if (!success)
            {
                return NotFound(new { success = false, message = "Alarm bulunamadı." });
            }

            return Ok(new { success = true, message = $"Alarm #{id} onaylandı." });
        }
    }
}
