using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using IndustrialDataLogger.Data;
using IndustrialDataLogger.Enums;
using IndustrialDataLogger.Models.DTOs;
using IndustrialDataLogger.Models.Entities;
using IndustrialDataLogger.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace IndustrialDataLogger.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class MachinesController : ControllerBase
    {
        private readonly IndustrialDbContext _dbContext;
        private readonly IDigitalTwinService _digitalTwinService;
        private readonly IEventLogService _eventLogService;
        private readonly ILogger<MachinesController> _logger;

        public MachinesController(
            IndustrialDbContext dbContext,
            IDigitalTwinService digitalTwinService,
            IEventLogService eventLogService,
            ILogger<MachinesController> logger)
        {
            _dbContext = dbContext;
            _digitalTwinService = digitalTwinService;
            _eventLogService = eventLogService;
            _logger = logger;
        }

        [HttpGet]
        public async Task<IActionResult> GetAllMachines(CancellationToken cancellationToken)
        {
            try
            {
                var machines = await _dbContext.Machines
                    .AsNoTracking()
                    .OrderBy(m => m.Id)
                    .ToListAsync(cancellationToken);

                return Ok(machines);
            }
            catch (Exception ex)
            {
                _logger.LogError("Makineler listelenirken hata: {Message}", ex.Message);
                return StatusCode(500, new { success = false, message = ex.Message });
            }
        }

        [HttpGet("{id}")]
        public async Task<IActionResult> GetMachineById(int id, CancellationToken cancellationToken)
        {
            try
            {
                var machine = await _dbContext.Machines
                    .AsNoTracking()
                    .FirstOrDefaultAsync(m => m.Id == id, cancellationToken);

                if (machine == null)
                {
                    return NotFound(new { success = false, message = $"Makine bulunamadı (ID: {id})" });
                }

                return Ok(machine);
            }
            catch (Exception ex)
            {
                _logger.LogError("Makine detayları getirilirken hata: {Message}", ex.Message);
                return StatusCode(500, new { success = false, message = ex.Message });
            }
        }

        [HttpPost]
        public async Task<IActionResult> CreateMachine([FromBody] CreateMachineDto dto, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(dto.MachineCode) || string.IsNullOrWhiteSpace(dto.Name))
            {
                return BadRequest(new { success = false, message = "Makine kodu ve adı zorunludur." });
            }

            try
            {
                bool exists = await _dbContext.Machines.AnyAsync(m => m.MachineCode == dto.MachineCode.Trim(), cancellationToken);
                if (exists)
                {
                    return BadRequest(new { success = false, message = $"'{dto.MachineCode}' kodlu makine zaten mevcut!" });
                }

                var machine = new Machine
                {
                    MachineCode = dto.MachineCode.Trim(),
                    Name = dto.Name.Trim(),
                    Type = string.IsNullOrWhiteSpace(dto.Type) ? "InjectionMolding" : dto.Type.Trim(),
                    PlcIp = string.IsNullOrWhiteSpace(dto.PlcIp) ? "192.168.0.1" : dto.PlcIp.Trim(),
                    Description = dto.Description?.Trim() ?? string.Empty,
                    IsActive = dto.IsActive,
                    CreatedAt = DateTime.UtcNow
                };

                await _dbContext.Machines.AddAsync(machine, cancellationToken);
                await _dbContext.SaveChangesAsync(cancellationToken);

                await _eventLogService.LogEventAsync("MACHINE_CREATED", $"Yeni makine sisteme kaydedildi: {machine.Name} ({machine.MachineCode})", AlarmSeverity.Info, "MachineManager", cancellationToken);

                return CreatedAtAction(nameof(GetMachineById), new { id = machine.Id }, machine);
            }
            catch (Exception ex)
            {
                _logger.LogError("Makine oluşturulurken hata: {Message}", ex.Message);
                return StatusCode(500, new { success = false, message = ex.Message });
            }
        }

        [HttpPut("{id}")]
        public async Task<IActionResult> UpdateMachine(int id, [FromBody] UpdateMachineDto dto, CancellationToken cancellationToken)
        {
            try
            {
                var machine = await _dbContext.Machines.FindAsync(new object[] { id }, cancellationToken);
                if (machine == null)
                {
                    return NotFound(new { success = false, message = $"Makine bulunamadı (ID: {id})" });
                }

                if (!string.IsNullOrWhiteSpace(dto.Name)) machine.Name = dto.Name.Trim();
                if (!string.IsNullOrWhiteSpace(dto.Type)) machine.Type = dto.Type.Trim();
                if (!string.IsNullOrWhiteSpace(dto.PlcIp)) machine.PlcIp = dto.PlcIp.Trim();
                if (dto.Description != null) machine.Description = dto.Description.Trim();
                if (dto.IsActive.HasValue) machine.IsActive = dto.IsActive.Value;

                machine.UpdatedAt = DateTime.UtcNow;

                await _dbContext.SaveChangesAsync(cancellationToken);
                await _eventLogService.LogEventAsync("MACHINE_UPDATED", $"Makine parametreleri güncellendi: {machine.Name} (ID: {id})", AlarmSeverity.Info, "MachineManager", cancellationToken);

                return Ok(machine);
            }
            catch (Exception ex)
            {
                _logger.LogError("Makine güncellenirken hata: {Message}", ex.Message);
                return StatusCode(500, new { success = false, message = ex.Message });
            }
        }

        [HttpGet("{id}/twin")]
        public async Task<IActionResult> GetMachineDigitalTwin(int id, CancellationToken cancellationToken)
        {
            try
            {
                var twinState = await _digitalTwinService.GetStateAsync(id, cancellationToken);
                return Ok(twinState);
            }
            catch (Exception ex)
            {
                _logger.LogError("Makine dijital ikiz durumu getirilirken hata: {Message}", ex.Message);
                return StatusCode(500, new { success = false, message = ex.Message });
            }
        }

        [HttpGet("overview")]
        public async Task<IActionResult> GetPlantOverview(CancellationToken cancellationToken)
        {
            try
            {
                var overview = await _digitalTwinService.GetPlantOverviewAsync(cancellationToken);
                return Ok(overview);
            }
            catch (Exception ex)
            {
                _logger.LogError("Fabrika dijital ikiz özeti getirilirken hata: {Message}", ex.Message);
                return StatusCode(500, new { success = false, message = ex.Message });
            }
        }
    }

    public class CreateMachineDto
    {
        public string MachineCode { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Type { get; set; } = "InjectionMolding";
        public string PlcIp { get; set; } = "192.168.0.1";
        public string? Description { get; set; }
        public bool IsActive { get; set; } = true;
    }

    public class UpdateMachineDto
    {
        public string? Name { get; set; }
        public string? Type { get; set; }
        public string? PlcIp { get; set; }
        public string? Description { get; set; }
        public bool? IsActive { get; set; }
    }
}
