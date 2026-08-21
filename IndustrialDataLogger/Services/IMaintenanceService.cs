using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using IndustrialDataLogger.Enums;
using IndustrialDataLogger.Models.DTOs;
using IndustrialDataLogger.Models.Entities;

namespace IndustrialDataLogger.Services
{
    public interface IMaintenanceService
    {
        Task<MaintenanceTaskResponseDto> CreateTaskAsync(CreateMaintenanceTaskDto dto, CancellationToken cancellationToken = default);
        Task<IReadOnlyList<MaintenanceTaskResponseDto>> GetTasksAsync(int? machineId = null, MaintenanceStatus? status = null, MaintenancePriority? priority = null, CancellationToken cancellationToken = default);
        Task<MaintenanceTaskResponseDto?> GetTaskByIdAsync(int id, CancellationToken cancellationToken = default);
        Task<MaintenanceTaskResponseDto?> UpdateTaskStatusAsync(int id, UpdateMaintenanceTaskStatusDto dto, CancellationToken cancellationToken = default);
        Task<MaintenanceTaskResponseDto?> AutoCreateTaskFromAlarmAsync(AlarmLog alarm, CancellationToken cancellationToken = default);
        Task<MaintenanceStatsDto> GetStatsAsync(int? machineId = null, CancellationToken cancellationToken = default);
    }
}
