using Microsoft.AspNetCore.Mvc;
using IndustrialDataLogger.Enums;
using IndustrialDataLogger.Services;
using System.Threading;
using System.Threading.Tasks;

namespace IndustrialDataLogger.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class EventsController : ControllerBase
    {
        private readonly IEventLogService _eventLogService;

        public EventsController(IEventLogService eventLogService)
        {
            _eventLogService = eventLogService;
        }

        /// <summary>
        /// Son sistem ve audit olaylarını filtrelenmiş ve kronolojik sırayla döner.
        /// </summary>
        [HttpGet("recent")]
        public async Task<IActionResult> GetRecentEvents(
            [FromQuery] int limit = 30,
            [FromQuery] string? eventType = null,
            [FromQuery] AlarmSeverity? severity = null,
            [FromQuery] int? machineId = null,
            CancellationToken cancellationToken = default)
        {
            var events = await _eventLogService.GetRecentEventsAsync(limit, eventType, severity, machineId, cancellationToken);
            return Ok(events);
        }

        /// <summary>
        /// Özel bir sistem/audit olayı kaydeder (Örn: Kullanıcı girişi, reçete değişikliği veya manuel kontrol).
        /// </summary>
        [HttpPost("log")]
        public async Task<IActionResult> LogCustomEvent(
            [FromBody] CustomEventRequest request,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(request.EventType) || string.IsNullOrWhiteSpace(request.Description))
            {
                return BadRequest(new { message = "EventType ve Description zorunludur." });
            }

            await _eventLogService.LogEventAsync(
                request.EventType,
                request.Description,
                request.Severity,
                request.Source ?? "UserAction",
                request.MachineId,
                cancellationToken);

            return Ok(new { success = true, message = "Olay başarıyla kaydedildi." });
        }
    }

    public class CustomEventRequest
    {
        public string EventType { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public AlarmSeverity Severity { get; set; } = AlarmSeverity.Info;
        public string? Source { get; set; } = "UserAction";
        public int? MachineId { get; set; }
    }
}
