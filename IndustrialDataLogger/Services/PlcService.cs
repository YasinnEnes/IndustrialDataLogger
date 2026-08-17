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

        public PlcService(IConfiguration configuration, ILogger<PlcService> logger)
        {
            _logger = logger;
            var ip = configuration["PlcSettings:IpAddress"] ?? "192.168.0.1";
            var rack = short.Parse(configuration["PlcSettings:Rack"] ?? "0");
            var slot = short.Parse(configuration["PlcSettings:Slot"] ?? "1");

            _plc = new Plc(CpuType.S71200, ip, rack, slot);
        }

        public bool IsConnected => _plc.IsConnected;

        // --- SENKRON METOTLAR (Worker için) ---
        public void Connect()
        {
            try
            {
                if (!_plc.IsConnected)
                {
                    _plc.Open();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"PLC senkron bağlantı hatası: {ex.Message}");
            }
        }

        public void Disconnect()
        {
            if (_plc.IsConnected)
            {
                _plc.Close();
            }
        }

        public SensorData ReadSensorData()
        {
            try
            {
                var tempBytes = _plc.ReadBytes(S7.Net.DataType.DataBlock, 1, 0, 4);
                var pressBytes = _plc.ReadBytes(S7.Net.DataType.DataBlock, 1, 4, 4);

                var statusObj = _plc.Read("DB1.DBX8.0");
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
            catch (Exception ex)
            {
                _logger.LogError($"Senkron veri okuma hatası: {ex.Message}");
                return new SensorData();
            }
        }

        // --- ASENKRON METOTLAR (Controller için) ---
        public async Task<bool> ConnectAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                if (!_plc.IsConnected)
                {
                    await _plc.OpenAsync(cancellationToken);
                }
                return _plc.IsConnected;
            }
            catch (Exception ex)
            {
                _logger.LogError($"PLC asenkron bağlantı hatası: {ex.Message}");
                return false;
            }
        }

        public Task DisconnectAsync()
        {
            if (_plc.IsConnected)
            {
                _plc.Close();
            }
            return Task.CompletedTask;
        }

        public async Task<SensorData> ReadSensorDataAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                var tempBytes = await _plc.ReadBytesAsync(S7.Net.DataType.DataBlock, 1, 0, 4, cancellationToken);
                var pressBytes = await _plc.ReadBytesAsync(S7.Net.DataType.DataBlock, 1, 4, 4, cancellationToken);

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
            catch (Exception ex)
            {
                _logger.LogError($"Asenkron veri okuma hatası: {ex.Message}");
                return new SensorData();
            }
        }

        // --- GELİŞMİŞ TİP GÜVENLİ YAZMA METOTLARI ---

        public async Task WriteBoolAsync(string variableAddress, bool value, CancellationToken cancellationToken = default)
        {
            if (!_plc.IsConnected) await _plc.OpenAsync(cancellationToken);
            await Task.Run(() => _plc.Write(variableAddress, value), cancellationToken);
        }

        public async Task WriteRealAsync(string variableAddress, float value, CancellationToken cancellationToken = default)
        {
            if (!_plc.IsConnected) await _plc.OpenAsync(cancellationToken);
            await Task.Run(() => _plc.Write(variableAddress, value), cancellationToken);
        }

        public async Task WriteIntAsync(string variableAddress, short value, CancellationToken cancellationToken = default)
        {
            if (!_plc.IsConnected) await _plc.OpenAsync(cancellationToken);
            await Task.Run(() => _plc.Write(variableAddress, value), cancellationToken);
        }

        public async Task WriteDIntAsync(string variableAddress, int value, CancellationToken cancellationToken = default)
        {
            if (!_plc.IsConnected) await _plc.OpenAsync(cancellationToken);
            await Task.Run(() => _plc.Write(variableAddress, value), cancellationToken);
        }

        public async Task WriteWordAsync(string variableAddress, ushort value, CancellationToken cancellationToken = default)
        {
            if (!_plc.IsConnected) await _plc.OpenAsync(cancellationToken);
            await Task.Run(() => _plc.Write(variableAddress, value), cancellationToken);
        }

        // Ana İstek Karşılayıcı (Request Tabanlı)
        public async Task<bool> WriteDataAsync(PlcWriteRequest request, CancellationToken cancellationToken = default)
        {
            try
            {
                if (!_plc.IsConnected)
                {
                    await _plc.OpenAsync(cancellationToken);
                }

                // Gelen değerin string olarak gelme ihtimaline karşı (Örn: Web arayüzünden "TRUE" veya "50.4" yazılması) tip dönüşümü
                object parsedValue = ParseValue(request.Value);

                // Tip odaklı yönlendirme metodunu çağırıyoruz
                await WriteTypedDataAsync(request.VariableName, parsedValue, cancellationToken);

                _logger.LogInformation($"PLC Başarıyla Yazıldı -> Adres: {request.VariableName}, Değer: {parsedValue} (Tip: {parsedValue?.GetType().Name})");
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError($"PLC yazma hatası -> Adres: {request.VariableName}, Hata: {ex.Message}");
                return false;
            }
        }

        // Değerin tipine göre doğru S7.Net Write metodunu seçen yönlendirici
        private async Task WriteTypedDataAsync(string variableAddress, object value, CancellationToken cancellationToken)
        {
            switch (value)
            {
                case bool bVal:
                    await WriteBoolAsync(variableAddress, bVal, cancellationToken);
                    break;
                case float fVal:
                    await WriteRealAsync(variableAddress, fVal, cancellationToken);
                    break;
                case double dVal:
                    await WriteRealAsync(variableAddress, (float)dVal, cancellationToken);
                    break;
                case short sVal:
                    await WriteIntAsync(variableAddress, sVal, cancellationToken);
                    break;
                case int iVal:
                    await WriteDIntAsync(variableAddress, iVal, cancellationToken);
                    break;
                case ushort usVal:
                    await WriteWordAsync(variableAddress, usVal, cancellationToken);
                    break;
                default:
                    await Task.Run(() => _plc.Write(variableAddress, value), cancellationToken);
                    break;
            }
        }

        // Web arayüzünden string olarak gelen değerleri (TRUE/FALSE, sayısal ifadeler) gerçek C# tiplerine dönüştüren yardımcı metot
        private object ParseValue(object rawValue)
        {
            if (rawValue == null) return null;

            string strVal = rawValue.ToString().Trim();

            // Bool kontrolü (TRUE / FALSE / 1 / 0)
            if (bool.TryParse(strVal, out bool boolRes)) return boolRes;
            if (strVal.Equals("1", StringComparison.OrdinalIgnoreCase)) return true;
            if (strVal.Equals("0", StringComparison.OrdinalIgnoreCase)) return false;

            // Float / Real kontrolü
            if (float.TryParse(strVal, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out float floatRes))
                return floatRes;

            // Integer kontrolü
            if (int.TryParse(strVal, out int intRes)) return intRes;

            return rawValue; // Dönüştürülemediyse olduğu gibi bırak
        }

        public async Task WriteDataAsync(string variableName, object value)
        {
            await WriteDataAsync(new PlcWriteRequest { VariableName = variableName, Value = value });
        }
    }
}