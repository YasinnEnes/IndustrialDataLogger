using System;
using System.Threading;
using System.Threading.Tasks;
using IndustrialDataLogger.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using S7.Net;

namespace IndustrialDataLogger.Services
{
    public class PlcService : IPlcService
    {
        private readonly Plc _plc;
        private readonly ILogger<PlcService> _logger;
        private readonly SemaphoreSlim _semaphore = new SemaphoreSlim(1, 1);

        public PlcService(IConfiguration configuration, ILogger<PlcService> logger)
        {
            _logger = logger;
            var ip = Environment.GetEnvironmentVariable("PLC_IP_ADDRESS")
                ?? configuration["PlcSettings:IpAddress"]
                ?? "192.168.0.1";
            var rackStr = Environment.GetEnvironmentVariable("PLC_RACK")
                ?? configuration["PlcSettings:Rack"]
                ?? "0";
            var slotStr = Environment.GetEnvironmentVariable("PLC_SLOT")
                ?? configuration["PlcSettings:Slot"]
                ?? "1";

            var rack = short.TryParse(rackStr, out var r) ? r : (short)0;
            var slot = short.TryParse(slotStr, out var s) ? s : (short)1;

            _plc = new Plc(CpuType.S71200, ip, rack, slot);
        }

        public bool IsConnected => _plc.IsConnected;

        public void Connect()
        {
            try
            {
                _semaphore.Wait();
                try
                {
                    if (!_plc.IsConnected) _plc.Open();
                }
                finally
                {
                    _semaphore.Release();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"PLC senkron bağlantı hatası: {ex.Message}");
            }
        }

        public void Disconnect()
        {
            try
            {
                _semaphore.Wait();
                try
                {
                    if (_plc.IsConnected) _plc.Close();
                }
                finally
                {
                    _semaphore.Release();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"PLC senkron kapatma hatası: {ex.Message}");
            }
        }

        public async Task<bool> ConnectAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                await _semaphore.WaitAsync(cancellationToken);
                try
                {
                    if (!_plc.IsConnected)
                    {
                        await _plc.OpenAsync(cancellationToken);
                    }
                    return _plc.IsConnected;
                }
                finally
                {
                    _semaphore.Release();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"PLC bağlantı hatası: {ex.Message}");
                return false;
            }
        }

        public async Task DisconnectAsync()
        {
            try
            {
                await _semaphore.WaitAsync();
                try
                {
                    if (_plc.IsConnected)
                    {
                        _plc.Close();
                    }
                }
                finally
                {
                    _semaphore.Release();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"PLC kapatma hatası: {ex.Message}");
            }
        }

        public async Task DisconnectAsync(CancellationToken cancellationToken)
        {
            await DisconnectAsync();
        }

        public SensorData? ReadSensorData()
        {
            // Senkron okuma gereksinimi için
            return ReadSensorDataAsync().GetAwaiter().GetResult();
        }

        public async Task<SensorData?> ReadSensorDataAsync(CancellationToken cancellationToken = default)
        {
            if (!_plc.IsConnected) return null;

            try
            {
                await _semaphore.WaitAsync(cancellationToken);
                try
                {
                    if (!_plc.IsConnected) return null;

                    var tempBytes = await _plc.ReadBytesAsync(DataType.DataBlock, 1, 0, 4, cancellationToken);
                    var pressBytes = await _plc.ReadBytesAsync(DataType.DataBlock, 1, 4, 4, cancellationToken);

                    var statusObj = await _plc.ReadAsync("DB1.DBX8.0", cancellationToken);
                    var status = statusObj != null && (bool)statusObj;

                    Array.Reverse(tempBytes);
                    Array.Reverse(pressBytes);

                    float tempFloat = BitConverter.ToSingle(tempBytes, 0);
                    float pressFloat = BitConverter.ToSingle(pressBytes, 0);

                    return new SensorData
                    {
                        Timestamp = DateTime.UtcNow,
                        Temperature = Math.Round((double)tempFloat, 2),
                        Pressure = Math.Round((double)pressFloat, 2),
                        MachineStatus = status,
                        ErrorCode = 0
                    };
                }
                finally
                {
                    _semaphore.Release();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"Veri okuma hatası: {ex.Message}");
                return null;
            }
        }

        public async Task<bool> WriteDataAsync(PlcWriteRequest request, CancellationToken cancellationToken = default)
        {
            if (!_plc.IsConnected) return false;

            try
            {
                await _semaphore.WaitAsync(cancellationToken);
                try
                {
                    if (!_plc.IsConnected) return false;
                    await Task.Run(() => _plc.Write(request.VariableName, request.Value), cancellationToken);
                    return true;
                }
                finally
                {
                    _semaphore.Release();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"Yazma hatası: {ex.Message}");
                return false;
            }
        }

        public async Task<bool> WriteDataAsync(string variableName, object value, CancellationToken cancellationToken = default)
        {
            return await WriteDataAsync(new PlcWriteRequest { VariableName = variableName, Value = value }, cancellationToken);
        }
    }
}