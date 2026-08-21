using System.Threading;
using System.Threading.Tasks;
using IndustrialDataLogger.Enums;
using IndustrialDataLogger.Models;
using IndustrialDataLogger.Models.DTOs;

namespace IndustrialDataLogger.Services
{
    public interface IDigitalTwinService
    {
        Task<DigitalTwinStateDto> GetStateAsync(CancellationToken cancellationToken = default);
        Task<DigitalTwinStateDto> GetStateAsync(int machineId, CancellationToken cancellationToken = default);
        
        Task<DigitalTwinStateDto> UpdateStateAsync(
            SensorData? sensorData,
            PlcConnectionState connectionState,
            CancellationToken cancellationToken = default);

        Task<DigitalTwinStateDto> UpdateStateAsync(
            SensorData? sensorData,
            PlcConnectionState connectionState,
            int machineId,
            CancellationToken cancellationToken = default);

        Task<PlantOverviewDto> GetPlantOverviewAsync(CancellationToken cancellationToken = default);

        (double score, HealthGrade grade, HealthScoreBreakdownDto breakdown) CalculateHealthScore(
            double temperature,
            double pressure,
            PlcConnectionState connectionState,
            int warningAlarmCount,
            int criticalAlarmCount);

        MachineOperationalStatus DetermineOperationalStatus(
            bool isConnected,
            PlcConnectionState connectionState,
            bool machineStatus,
            short errorCode,
            int criticalAlarmCount);
    }
}
