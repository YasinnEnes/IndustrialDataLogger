using System;
using System.Threading;
using System.Threading.Tasks;
using IndustrialDataLogger.Enums;
using IndustrialDataLogger.Models;

namespace IndustrialDataLogger.Services
{
    public class MockPlcService : IPlcService
    {
        private readonly Random _random = new Random();
        private double _currentTemp = 42.0;
        private double _currentPressure = 4.8;
        private bool _isMachineRunning = true;
        private int _stepCount = 0;

        public SimulationScenario CurrentScenario { get; private set; } = SimulationScenario.Normal;
        public bool IsConnected { get; private set; } = true;

        public void SetScenario(SimulationScenario scenario)
        {
            CurrentScenario = scenario;
            _stepCount = 0;

            if (scenario == SimulationScenario.Normal)
            {
                _currentTemp = 42.0;
                _currentPressure = 4.8;
                _isMachineRunning = true;
                IsConnected = true;
            }
            else if (scenario == SimulationScenario.Overheating)
            {
                _currentTemp = 76.0; // Hızlı ve belirgin tırmanış için 76°C'den başlasın
                _currentPressure = 5.0;
                _isMachineRunning = true;
                IsConnected = true;
            }
            else if (scenario == SimulationScenario.HighPressure)
            {
                _currentTemp = 45.0;
                _currentPressure = 7.6; // Hızlı ve belirgin tırmanış için 7.6 bar'dan başlasın
                _isMachineRunning = true;
                IsConnected = true;
            }
            else if (scenario == SimulationScenario.MachineStop)
            {
                _isMachineRunning = false;
                IsConnected = true;
            }
            else if (scenario == SimulationScenario.PlcDisconnect || scenario == SimulationScenario.PlcFailure)
            {
                IsConnected = false;
            }

            Console.WriteLine($"[MOCK PLC] Simülasyon Senaryosu Değiştirildi: {scenario} (IsConnected={IsConnected})");
        }

        public void Connect()
        {
            IsConnected = true;
        }

        public void Disconnect()
        {
            IsConnected = false;
        }

        public Task<bool> ConnectAsync(CancellationToken cancellationToken = default)
        {
            IsConnected = true;
            return Task.FromResult(true);
        }

        public Task DisconnectAsync(CancellationToken cancellationToken = default)
        {
            IsConnected = false;
            return Task.CompletedTask;
        }

        public Task DisconnectAsync()
        {
            IsConnected = false;
            return Task.CompletedTask;
        }

        public SensorData? ReadSensorData()
        {
            if (!IsConnected || CurrentScenario == SimulationScenario.PlcDisconnect)
            {
                return null; // Bağlantı kopması simülasyonu
            }

            _stepCount++;

            switch (CurrentScenario)
            {
                case SimulationScenario.Overheating:
                    // 25°C'den 95°C+ üzerine kademeli tırmanış
                    _currentTemp = Math.Min(98.0, _currentTemp + 4.5 + (_random.NextDouble() * 1.5));
                    _currentPressure = Math.Round(5.0 + (_random.NextDouble() * 0.8 - 0.4), 2);
                    _isMachineRunning = true;
                    break;

                case SimulationScenario.HighPressure:
                    // 4.0 bar'dan 9.8 bar üzerine kademeli tırmanış
                    _currentPressure = Math.Min(9.9, _currentPressure + 0.6 + (_random.NextDouble() * 0.3));
                    _currentTemp = Math.Round(45.0 + (_random.NextDouble() * 2.0 - 1.0), 2);
                    _isMachineRunning = true;
                    break;

                case SimulationScenario.MachineStop:
                    // Makine durdu, sıcaklık ve basınç ortam seviyesine düşüyor
                    _isMachineRunning = false;
                    _currentTemp = Math.Max(22.0, _currentTemp - 2.5);
                    _currentPressure = Math.Max(0.2, _currentPressure - 0.8);
                    break;

                case SimulationScenario.Normal:
                default:
                    // Normal çalışma aralığı (Temp: 35-50°C, Press: 4-6 bar)
                    _isMachineRunning = true;
                    _currentTemp = Math.Round(35.0 + (_random.NextDouble() * 15.0), 2);
                    _currentPressure = Math.Round(4.0 + (_random.NextDouble() * 2.5), 2);
                    break;
            }

            return new SensorData
            {
                Temperature = Math.Round(_currentTemp, 2),
                Pressure = Math.Round(_currentPressure, 2),
                MachineStatus = _isMachineRunning,
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
            if (!IsConnected || CurrentScenario == SimulationScenario.PlcDisconnect || CurrentScenario == SimulationScenario.PlcFailure)
            {
                Console.WriteLine("[MOCK PLC] Hata: PLC çevrimdışı olduğu için veri yazılamadı.");
                return false;
            }

            Console.WriteLine($"[MOCK PLC] Adres: {request?.VariableName} | Değer: {request?.Value}");
            await Task.Delay(50, cancellationToken);
            return true;
        }

        public async Task<bool> WriteDataAsync(string variableName, object value, CancellationToken cancellationToken = default)
        {
            return await WriteDataAsync(new PlcWriteRequest { VariableName = variableName, Value = value }, cancellationToken);
        }
    }
}