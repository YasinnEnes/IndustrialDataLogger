using Microsoft.AspNetCore.Mvc;
using IndustrialDataLogger.Models.DTOs;
using IndustrialDataLogger.Models.Entities;
using IndustrialDataLogger.Services;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace IndustrialDataLogger.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class AlarmRulesController : ControllerBase
    {
        private readonly IAlarmService _alarmService;

        public AlarmRulesController(IAlarmService alarmService)
        {
            _alarmService = alarmService;
        }

        /// <summary>
        /// Konfigüre edilebilir tüm alarm kurallarını listeler (İsteğe bağlı makine ve aktiflik filtresi).
        /// </summary>
        [HttpGet]
        public async Task<IActionResult> GetRules(
            [FromQuery] int? machineId = null,
            [FromQuery] bool? enabledOnly = null,
            CancellationToken cancellationToken = default)
        {
            var rules = await _alarmService.GetRulesAsync(machineId, enabledOnly, cancellationToken);
            var response = rules.Select(MapToDto).ToList();
            return Ok(response);
        }

        /// <summary>
        /// Belirtilen ID'ye sahip alarm kuralının detayını getirir.
        /// </summary>
        [HttpGet("{id}")]
        public async Task<IActionResult> GetRuleById(int id, CancellationToken cancellationToken = default)
        {
            var rule = await _alarmService.GetRuleByIdAsync(id, cancellationToken);
            if (rule == null)
            {
                return NotFound(new { success = false, message = $"Alarm kuralı #{id} bulunamadı." });
            }

            return Ok(MapToDto(rule));
        }

        /// <summary>
        /// Yeni bir alarm kuralı oluşturur.
        /// </summary>
        [HttpPost]
        public async Task<IActionResult> CreateRule(
            [FromBody] CreateAlarmRuleDto dto,
            CancellationToken cancellationToken = default)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            var rule = new AlarmRule
            {
                MachineId = dto.MachineId,
                RuleName = dto.RuleName,
                Metric = dto.Metric,
                Operator = dto.Operator,
                Threshold = dto.Threshold,
                Severity = dto.Severity,
                AlarmType = dto.AlarmType,
                MessageTemplate = string.IsNullOrWhiteSpace(dto.MessageTemplate)
                    ? "Eşik aşıldı: {value} (Eşik: {threshold})"
                    : dto.MessageTemplate,
                Enabled = dto.Enabled
            };

            var created = await _alarmService.CreateRuleAsync(rule, cancellationToken);
            return CreatedAtAction(nameof(GetRuleById), new { id = created.Id }, MapToDto(created));
        }

        /// <summary>
        /// Mevcut bir alarm kuralını günceller.
        /// </summary>
        [HttpPut("{id}")]
        public async Task<IActionResult> UpdateRule(
            int id,
            [FromBody] UpdateAlarmRuleDto dto,
            CancellationToken cancellationToken = default)
        {
            var existing = await _alarmService.GetRuleByIdAsync(id, cancellationToken);
            if (existing == null)
            {
                return NotFound(new { success = false, message = $"Alarm kuralı #{id} bulunamadı." });
            }

            if (dto.MachineId.HasValue) existing.MachineId = dto.MachineId;
            if (!string.IsNullOrWhiteSpace(dto.RuleName)) existing.RuleName = dto.RuleName;
            if (!string.IsNullOrWhiteSpace(dto.Metric)) existing.Metric = dto.Metric;
            if (dto.Operator.HasValue) existing.Operator = dto.Operator.Value;
            if (dto.Threshold.HasValue) existing.Threshold = dto.Threshold.Value;
            if (dto.Severity.HasValue) existing.Severity = dto.Severity.Value;
            if (!string.IsNullOrWhiteSpace(dto.AlarmType)) existing.AlarmType = dto.AlarmType;
            if (!string.IsNullOrWhiteSpace(dto.MessageTemplate)) existing.MessageTemplate = dto.MessageTemplate;
            if (dto.Enabled.HasValue) existing.Enabled = dto.Enabled.Value;

            var success = await _alarmService.UpdateRuleAsync(existing, cancellationToken);
            if (!success)
            {
                return StatusCode(500, new { success = false, message = "Kural güncellenirken bir hata oluştu." });
            }

            return Ok(MapToDto(existing));
        }

        /// <summary>
        /// Belirtilen alarm kuralını siler.
        /// </summary>
        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteRule(int id, CancellationToken cancellationToken = default)
        {
            var success = await _alarmService.DeleteRuleAsync(id, cancellationToken);
            if (!success)
            {
                return NotFound(new { success = false, message = $"Alarm kuralı #{id} bulunamadı." });
            }

            return Ok(new { success = true, message = $"Alarm kuralı #{id} başarıyla silindi." });
        }

        /// <summary>
        /// Belirtilen alarm kuralının aktiflik durumunu (Enabled/Disabled) değiştirir.
        /// </summary>
        [HttpPatch("{id}/toggle")]
        public async Task<IActionResult> ToggleRule(
            int id,
            [FromQuery] bool enabled,
            CancellationToken cancellationToken = default)
        {
            var success = await _alarmService.ToggleRuleAsync(id, enabled, cancellationToken);
            if (!success)
            {
                return NotFound(new { success = false, message = $"Alarm kuralı #{id} bulunamadı." });
            }

            return Ok(new { success = true, message = $"Alarm kuralı #{id} durumu {(enabled ? "aktif" : "pasif")} yapıldı." });
        }

        private static AlarmRuleResponseDto MapToDto(AlarmRule r)
        {
            return new AlarmRuleResponseDto
            {
                Id = r.Id,
                MachineId = r.MachineId,
                MachineName = r.Machine?.Name,
                RuleName = r.RuleName,
                Metric = r.Metric,
                Operator = r.Operator.ToString(),
                Threshold = r.Threshold,
                Severity = r.Severity.ToString(),
                AlarmType = r.AlarmType,
                MessageTemplate = r.MessageTemplate,
                Enabled = r.Enabled,
                CreatedAt = r.CreatedAt,
                UpdatedAt = r.UpdatedAt
            };
        }
    }
}
