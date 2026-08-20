using System;
using System.Threading;
using System.Threading.Tasks;
using IndustrialDataLogger.Enums;
using IndustrialDataLogger.Models;

namespace IndustrialDataLogger.Services
{
    public interface IPlcConnectionManager
    {
        PlcConnectionState CurrentState { get; }
        bool IsConnected { get; }
        SimulationScenario CurrentScenario { get; }

        event Action<PlcConnectionEvent, string>? OnConnectionEvent;

        Task<bool> ConnectAsync(CancellationToken cancellationToken = default);
        Task DisconnectAsync();
        Task SetModeAsync(bool isSimulation, CancellationToken cancellationToken = default);
        void SetSimulationScenario(SimulationScenario scenario);
        Task<SensorData?> ReadDataAsync(CancellationToken cancellationToken = default);
        Task<bool> WriteDataAsync(PlcWriteRequest request, CancellationToken cancellationToken = default);
    }
}