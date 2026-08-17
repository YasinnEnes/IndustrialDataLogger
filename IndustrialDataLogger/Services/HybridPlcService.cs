using System;
using System.Threading;
using System.Threading.Tasks;
using IndustrialDataLogger.Models;
using Microsoft.Extensions.Logging;

namespace IndustrialDataLogger.Services
{
    public class HybridPlcService : IPlcService
    {
        private readonly MockPlcService _mockService;
        private readonly PlcService _realService;
        private readonly ILogger<HybridPlcService> _logger;

        public static bool IsSimulationMode { get; set; } = true;

        public HybridPlcService(MockPlcService mockService, PlcService realService, ILogger<HybridPlcService> logger)
        {
            _mockService = mockService;
            _realService = realService;
            _logger = logger;
        }

        public bool IsConnected => IsSimulationMode ? _mockService.IsConnected : _realService.IsConnected;

        // Tek ve Merkezi Okuma Metodu (Bağlantı Kontrolü Dahil)
        public async Task<SensorData> ReadSensorDataAsync(CancellationToken cancellationToken = default)
        {
            // BAĞLANTI KONTROLÜ: Bağlı değilse kesinlikle veri üretilmez (Kaynakta engelleme)
            if (!IsConnected)
            {
                _logger.LogWarning("PLC bağlı değil, veri akışı durduruldu.");
                return null;
            }

            return IsSimulationMode ? await _mockService.ReadSensorDataAsync(cancellationToken)
                                    : await _realService.ReadSensorDataAsync(cancellationToken);
        }

        public SensorData ReadSensorData()
        {
            if (!IsConnected) return null;
            return IsSimulationMode ? new SensorData() : null;
        }

        public void Connect()
        {
            if (IsSimulationMode) _mockService.Connect();
            else _realService.Connect();
        }

        public Task<bool> ConnectAsync(CancellationToken cancellationToken = default)
            => IsSimulationMode ? _mockService.ConnectAsync(cancellationToken) : _realService.ConnectAsync(cancellationToken);

        public void Disconnect()
        {
            if (IsSimulationMode)
                _mockService.Disconnect();
            else
                _realService.Disconnect();
        }

        public Task DisconnectAsync(CancellationToken cancellationToken = default)
        {
            if (IsSimulationMode)
            {
                _mockService.Disconnect();
            }
            else
            {
                _realService.Disconnect();
            }
            return Task.CompletedTask;
        }

        public Task WriteDataAsync(string variableName, object value)
        {
            if (!IsConnected) throw new InvalidOperationException("PLC bağlı değil.");

            if (IsSimulationMode)
            {
                return _mockService.WriteDataAsync(variableName, value);
            }
            else
            {
                return _realService.WriteDataAsync(variableName, value);
            }
        }

        public Task WriteDataAsync(PlcWriteRequest request, CancellationToken cancellationToken = default)
        {
            if (!IsConnected) throw new InvalidOperationException("PLC bağlı değil.");

            if (IsSimulationMode)
            {
                return _mockService.WriteDataAsync(request, cancellationToken);
            }
            else
            {
                return _realService.WriteDataAsync(request.VariableName, request.Value);
            }
        }

        async Task<bool> IPlcService.WriteDataAsync(PlcWriteRequest request, CancellationToken cancellationToken)
        {
            if (!IsConnected) return false;
            await WriteDataAsync(request, cancellationToken);
            return true;
        }

        public Task DisconnectAsync()
        {
            throw new NotImplementedException();
        }
    }
}