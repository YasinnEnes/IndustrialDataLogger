using System;
using System.Threading;
using System.Threading.Tasks;
using IndustrialDataLogger.Enums;
using IndustrialDataLogger.Models;
using Microsoft.Extensions.Logging;

namespace IndustrialDataLogger.Services
{
    public class PlcConnectionManager : IPlcConnectionManager
    {
        private readonly PlcService _realPlcService;
        private readonly MockPlcService _mockPlcService;
        private readonly ILogger<PlcConnectionManager> _logger;
        private readonly object _stateLock = new object();
        private CancellationTokenSource? _reconnectCts;
        private bool _userRequestedDisconnect = true;

        // KRİTİK: Site ilk açıldığında başlangıç durumu kesinlikle Disconnected olmalıdır.
        public PlcConnectionState CurrentState { get; private set; } = PlcConnectionState.Disconnected;
        public bool IsConnected => CurrentState == PlcConnectionState.Connected;

        public static bool IsSimulationMode { get; set; } = true;
        public static bool SimulateConnectionError { get; set; } = false;

        public PlcConnectionManager(
            PlcService realPlcService,
            MockPlcService mockPlcService,
            ILogger<PlcConnectionManager> logger)
        {
            _realPlcService = realPlcService;
            _mockPlcService = mockPlcService;
            _logger = logger;
        }

        public async Task<bool> ConnectAsync(CancellationToken cancellationToken = default)
        {
            if (IsConnected || CurrentState == PlcConnectionState.Connecting) return true;

            StopAutoReconnect();
            _userRequestedDisconnect = false;

            try
            {
                CurrentState = PlcConnectionState.Connecting;
                _logger.LogInformation("PLC bağlantısı kuruluyor...");

                if (SimulateConnectionError)
                {
                    throw new InvalidOperationException("Bağlantı Kurulamadı: Ağ kablosu takılı değil veya TIA Portal projesi uyuşmuyor!");
                }

                if (IsSimulationMode)
                {
                    await _mockPlcService.ConnectAsync(cancellationToken);
                }
                else
                {
                    var success = await _realPlcService.ConnectAsync(cancellationToken);
                    if (!success)
                    {
                        throw new InvalidOperationException("Gerçek PLC soket bağlantısı (S7.Net) kurulamadı!");
                    }
                }

                CurrentState = PlcConnectionState.Connected;
                _logger.LogInformation("PLC bağlantısı başarıyla kuruldu.");
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError("Bağlantı Hatası: {Message}", ex.Message);
                CurrentState = PlcConnectionState.Disconnected;
                return false;
            }
        }

        public async Task SetModeAsync(bool isSimulation, CancellationToken cancellationToken = default)
        {
            if (IsSimulationMode == isSimulation) return;

            bool wasConnected = IsConnected;
            if (wasConnected)
            {
                await DisconnectAsync();
            }

            IsSimulationMode = isSimulation;

            if (wasConnected)
            {
                await ConnectAsync(cancellationToken);
            }
        }

        public async Task DisconnectAsync()
        {
            _userRequestedDisconnect = true;
            StopAutoReconnect();

            if (CurrentState == PlcConnectionState.Disconnected) return;

            CurrentState = PlcConnectionState.Disconnecting;
            _logger.LogInformation("PLC bağlantısı tamamen kesiliyor...");

            await _mockPlcService.DisconnectAsync();
            await _realPlcService.DisconnectAsync();

            CurrentState = PlcConnectionState.Disconnected;
            _logger.LogInformation("PLC bağlantısı kapatıldı. Durum: Disconnected.");
        }

        public async Task<SensorData?> ReadDataAsync(CancellationToken cancellationToken = default)
        {
            if (!IsConnected) return null;

            if (IsSimulationMode)
            {
                if (!_mockPlcService.IsConnected) await _mockPlcService.ConnectAsync(cancellationToken);
                return await _mockPlcService.ReadSensorDataAsync(cancellationToken);
            }
            else
            {
                var data = await _realPlcService.ReadSensorDataAsync(cancellationToken);
                if (data == null)
                {
                    // Bağlantı koptu (kablo çekildi / soket kapandı)
                    if (!_userRequestedDisconnect && CurrentState == PlcConnectionState.Connected)
                    {
                        HandleUnexpectedDisconnect();
                    }
                    return null;
                }
                return data;
            }
        }

        public async Task<bool> WriteDataAsync(PlcWriteRequest request, CancellationToken cancellationToken = default)
        {
            if (!IsConnected) return false;

            return IsSimulationMode
                ? await _mockPlcService.WriteDataAsync(request, cancellationToken)
                : await _realPlcService.WriteDataAsync(request, cancellationToken);
        }

        private void HandleUnexpectedDisconnect()
        {
            lock (_stateLock)
            {
                if (CurrentState != PlcConnectionState.Connected) return;

                CurrentState = PlcConnectionState.Reconnecting;
                _logger.LogWarning("PLC bağlantısı beklenmedik şekilde koptu! Durum: Reconnecting. Otomatik toparlanma başlatılıyor...");

                StartAutoReconnect();
            }
        }

        private void StartAutoReconnect()
        {
            StopAutoReconnect();

            _reconnectCts = new CancellationTokenSource();
            var token = _reconnectCts.Token;

            _ = Task.Run(async () =>
            {
                while (!token.IsCancellationRequested && !_userRequestedDisconnect && CurrentState == PlcConnectionState.Reconnecting)
                {
                    try
                    {
                        await Task.Delay(3000, token);
                        if (token.IsCancellationRequested || _userRequestedDisconnect) break;

                        _logger.LogInformation("PLC'ye yeniden bağlanmayı deniyor (Auto-Reconnect)...");

                        if (IsSimulationMode)
                        {
                            await _mockPlcService.ConnectAsync(token);
                            CurrentState = PlcConnectionState.Connected;
                            _logger.LogInformation("Simülasyon bağlantısı otomatik olarak yeniden kuruldu!");
                            break;
                        }
                        else
                        {
                            var connected = await _realPlcService.ConnectAsync(token);
                            if (connected)
                            {
                                CurrentState = PlcConnectionState.Connected;
                                _logger.LogInformation("Gerçek PLC bağlantısı otomatik olarak yeniden kuruldu (ONLINE)!");
                                break;
                            }
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning("Yeniden bağlanma denemesi başarısız oldu: {Message}. Tekrar denenecek...", ex.Message);
                    }
                }
            }, token);
        }

        private void StopAutoReconnect()
        {
            if (_reconnectCts != null)
            {
                try
                {
                    _reconnectCts.Cancel();
                    _reconnectCts.Dispose();
                }
                catch { }
                finally
                {
                    _reconnectCts = null;
                }
            }
        }
    }
}