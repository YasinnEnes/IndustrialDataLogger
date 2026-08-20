using System.Threading;
using System.Threading.Tasks;
using IndustrialDataLogger.Models;

namespace IndustrialDataLogger.Services
{
    public interface IPlcService
    {
        // Ortak Özellik
        bool IsConnected { get; }

        // Senkron Metotlar (Worker.cs tarafından kullanılır)
        void Connect();
        void Disconnect();
        SensorData? ReadSensorData();

        // Asenkron Metotlar (SensorController.cs tarafından kullanılır)
        Task<bool> ConnectAsync(CancellationToken cancellationToken = default);
        Task DisconnectAsync();
        Task DisconnectAsync(CancellationToken cancellationToken);
        Task<SensorData?> ReadSensorDataAsync(CancellationToken cancellationToken = default);
        Task<bool> WriteDataAsync(PlcWriteRequest request, CancellationToken cancellationToken = default);
        Task<bool> WriteDataAsync(string variableName, object value, CancellationToken cancellationToken = default);
    }
}