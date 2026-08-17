using System;
using System.Threading;
using System.Threading.Tasks;
using IndustrialDataLogger.Models;

namespace IndustrialDataLogger.Services
{
    public class MockPlcService : IPlcService
    {
        private readonly Random _random = new Random();

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

        public Task<SensorData> ReadSensorDataAsync()
        {
            var data = new SensorData
            {
                Temperature = Math.Round(_random.NextDouble() * 60 + 20, 2),
                Pressure = Math.Round(_random.NextDouble() * 9 + 1, 2),
                MachineStatus = _random.Next(2) == 1,
                Timestamp = DateTime.UtcNow,
                ErrorCode = 0
            };

            return Task.FromResult(data);
        }

        public Task<SensorData> ReadSensorDataAsync(CancellationToken cancellationToken = default)
        {
            return ReadSensorDataAsync();
        }

        public Task WriteDataAsync(string variableName, object value)
        {
            Console.WriteLine($"[MOCK PLC] Adres: {variableName} | Değer: {value}");
            return Task.CompletedTask;
        }

        public Task WriteDataAsync(PlcWriteRequest request, CancellationToken cancellationToken = default)
        {
            Console.WriteLine($"[MOCK PLC] Adres: {request?.VariableName} | Değer: {request?.Value}");
            return Task.CompletedTask;
        }

        public SensorData ReadSensorData()
        {
            throw new NotImplementedException();
        }

        public Task DisconnectAsync()
        {
            throw new NotImplementedException();
        }

        Task<bool> IPlcService.WriteDataAsync(PlcWriteRequest request, CancellationToken cancellationToken)
        {
            throw new NotImplementedException();
        }
    }
}