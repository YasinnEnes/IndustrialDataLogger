using System.Threading;
using System.Threading.Tasks;
using IndustrialDataLogger.Enums;
using IndustrialDataLogger.Models.DTOs;
using IndustrialDataLogger.Services;
using Microsoft.AspNetCore.Mvc;

namespace IndustrialDataLogger.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class MaintenanceController : ControllerBase
    {
        private readonly IMaintenanceService _maintenanceService;

        public MaintenanceController(IMaintenanceService maintenanceService)
        {
            _maintenanceService = maintenanceService;
        }

        /// <summary>
        /// Bakım iş emirlerini listeler. Makine, durum ve öncelik filtreleri uygulanabilir.
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> GetTasks(
            [FromQuery] int? machineId = null,
            [FromQuery] MaintenanceStatus? status = null,
            [FromQuery] MaintenancePriority? priority = null,
            CancellationToken cancellationToken = default)
        {
            var tasks = await _maintenanceService.GetTasksAsync(machineId, status, priority, cancellationToken);
            return Ok(tasks);
        }

        /// <summary>
        /// Belirtilen ID'ye sahip bakım iş emrinin detaylarını döner.
        /// </summary>
        [HttpGet("{id:int}")]
        public async Task<IActionResult> GetTaskById(int id, CancellationToken cancellationToken = default)
        {
            var task = await _maintenanceService.GetTaskByIdAsync(id, cancellationToken);
            if (task == null)
            {
                return NotFound(new { message = $"İş emri bulunamadı: #{id}" });
            }
            return Ok(task);
        }

        /// <summary>
        /// Yeni manuel bakım iş emri oluşturur (Yetkili kullanıcılar).
        /// </summary>
        [HttpPost]
        public async Task<IActionResult> CreateTask(
            [FromBody] CreateMaintenanceTaskDto dto,
            CancellationToken cancellationToken = default)
        {
            if (dto == null || string.IsNullOrWhiteSpace(dto.Reason))
            {
                return BadRequest(new { message = "İş emri nedeni (Reason) boş bırakılamaz." });
            }

            var created = await _maintenanceService.CreateTaskAsync(dto, cancellationToken);
            return CreatedAtAction(nameof(GetTaskById), new { id = created.Id }, created);
        }

        /// <summary>
        /// Bakım iş emrinin durumunu günceller (Örn: InProgress -> Resolved).
        /// </summary>
        [HttpPut("{id:int}/status")]
        public async Task<IActionResult> UpdateStatus(
            int id,
            [FromBody] UpdateMaintenanceTaskStatusDto dto,
            CancellationToken cancellationToken = default)
        {
            if (dto == null)
            {
                return BadRequest(new { message = "Geçersiz durum güncelleme parametresi." });
            }

            var updated = await _maintenanceService.UpdateTaskStatusAsync(id, dto, cancellationToken);
            if (updated == null)
            {
                return NotFound(new { message = $"Güncellenecek iş emri bulunamadı: #{id}" });
            }

            return Ok(updated);
        }

        /// <summary>
        /// Bakım yönetimi KPI istatistiklerini (Açık, İncelenen, Çözülen, Ortalama Çözüm Süresi) döner.
        /// </summary>
        [HttpGet("stats")]
        public async Task<IActionResult> GetStats(
            [FromQuery] int? machineId = null,
            CancellationToken cancellationToken = default)
        {
            var stats = await _maintenanceService.GetStatsAsync(machineId, cancellationToken);
            return Ok(stats);
        }
    }
}
