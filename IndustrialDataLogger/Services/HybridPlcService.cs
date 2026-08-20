using System;
using System.Threading;
using System.Threading.Tasks;
using IndustrialDataLogger.Enums;
using IndustrialDataLogger.Models;
using Microsoft.Extensions.Logging;

namespace IndustrialDataLogger.Services
{
    public class HybridPlcService : IPlcService
    {
        private readonly IPlcConnectionManager _connectionManager;
        private readonly ILogger<HybridPlcService> _logger;

        public static bool IsSimulationMode
        {
            get => PlcConnectionManager.IsSimulationMode;
            set => PlcConnectionManager.IsSimulationMode = value;
        }

        public bool IsConnected => _connectionManager.IsConnected;

        public HybridPlcService(IPlcConnectionManager connectionManager, ILogger<HybridPlcService> logger)
        {
            _connectionManager = connectionManager;
            _logger = logger;
        }

        public async Task<SensorData?> ReadSensorDataAsync(CancellationToken cancellationToken = default)
        {
            return await _connectionManager.ReadDataAsync(cancellationToken);
        }

        public SensorData? ReadSensorData()
        {
            return _connectionManager.ReadDataAsync(default).GetAwaiter().GetResult();
        }

        public async Task<bool> WriteDataAsync(PlcWriteRequest request, CancellationToken cancellationToken = default)
        {
            return await _connectionManager.WriteDataAsync(request, cancellationToken);
        }

        public async Task<bool> WriteDataAsync(string variableName, object value, CancellationToken cancellationToken = default)
        {
            var request = new PlcWriteRequest { VariableName = variableName, Value = value };
            return await WriteDataAsync(request, cancellationToken);
        }

        public void Connect()
        {
            _connectionManager.ConnectAsync(default).GetAwaiter().GetResult();
        }

        public async Task<bool> ConnectAsync(CancellationToken cancellationToken = default)
        {
            return await _connectionManager.ConnectAsync(cancellationToken);
        }

        public void Disconnect()
        {
            _connectionManager.DisconnectAsync().GetAwaiter().GetResult();
        }

        public async Task DisconnectAsync()
        {
            await _connectionManager.DisconnectAsync();
        }

        public async Task DisconnectAsync(CancellationToken cancellationToken)
        {
            await _connectionManager.DisconnectAsync();
        }
    }
}