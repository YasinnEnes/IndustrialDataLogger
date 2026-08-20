using System;
using System.Threading;
using System.Threading.Tasks;
using IndustrialDataLogger.Models;

namespace IndustrialDataLogger.Services
{
    public class MockPlcService : IPlcService
    {
        private readonly Random _random = new Random();

        // Mock servis her zaman simülasyon gereği bağlı kabul edilebilir veya bağlantı yöneticisine bağlanabilir
        public bool IsConnected => true;

        public void Connect() { }

        public void Disconnect() { }

        public Task<bool> ConnectAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(true);
        }

        public Task DisconnectAsync(CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public Task DisconnectAsync()
        {
            return Task.CompletedTask;
        }

        public SensorData? ReadSensorData()
        {
            return new SensorData
            {
                Temperature = Math.Round(_random.NextDouble() * 60 + 20, 2),
                Pressure = Math.Round(_random.NextDouble() * 9 + 1, 2),
                MachineStatus = _random.Next(2) == 1,
                Timestamp = DateTime.UtcNow,
                ErrorCode = 0
            };
        }

        public Task<SensorData?> ReadSensorDataAsync(CancellationToken cancellationToken = default)
        {
            SensorData? data = ReadSensorData();
            return Task.FromResult(data);
        }

        public async Task<bool> WriteDataAsync(PlcWriteRequest request, CancellationToken cancellationToken = default)
        {
            Console.WriteLine($"[MOCK PLC] Adres: {request?.VariableName} | Değer: {request?.Value}");
            await Task.Delay(100, cancellationToken);
            return true;
        }

        public async Task<bool> WriteDataAsync(string variableName, object value, CancellationToken cancellationToken = default)
        {
            Console.WriteLine($"[MOCK PLC] Adres: {variableName} | Değer: {value}");
            await Task.Delay(100, cancellationToken);
            return true;
        }
    }
}