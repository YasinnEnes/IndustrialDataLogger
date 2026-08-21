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

        public event Action<PlcConnectionEvent, string>? OnConnectionEvent;

        // KRİTİK: Başlangıç durumu kesinlikle Disconnected olmalıdır.
        public PlcConnectionState CurrentState { get; private set; } = PlcConnectionState.Disconnected;
        public bool IsConnected => CurrentState == PlcConnectionState.Connected;

        public SimulationScenario CurrentScenario => _mockPlcService.CurrentScenario;

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

        public void SetSimulationScenario(SimulationScenario scenario)
        {
            _mockPlcService.SetScenario(scenario);
            _logger.LogInformation("Simülasyon Senaryosu Güncellendi: {Scenario}", scenario);

            if (scenario == SimulationScenario.PlcDisconnect && IsConnected)
            {
                // Bağlantı kopması senaryosu
                HandleUnexpectedDisconnect();
            }
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
                OnConnectionEvent?.Invoke(PlcConnectionEvent.PlcConnected, "PLC bağlantısı başarıyla kuruldu.");
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError("Bağlantı Hatası: {Message}", ex.Message);
                CurrentState = PlcConnectionState.Disconnected;
                OnConnectionEvent?.Invoke(PlcConnectionEvent.PlcDisconnected, $"Bağlantı kurulamadı: {ex.Message}");
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

            if (_mockPlcService != null) await _mockPlcService.DisconnectAsync();
            if (_realPlcService != null) await _realPlcService.DisconnectAsync();

            CurrentState = PlcConnectionState.Disconnected;
            _logger.LogInformation("PLC bağlantısı kapatıldı. Durum: Disconnected.");
            OnConnectionEvent?.Invoke(PlcConnectionEvent.PlcDisconnected, "PLC bağlantısı operatör tarafından kapatıldı.");
        }

        public async Task<SensorData?> ReadDataAsync(CancellationToken cancellationToken = default)
        {
            if (!IsConnected) return null;

            if (IsSimulationMode)
            {
                if (!_mockPlcService.IsConnected && CurrentScenario != SimulationScenario.PlcDisconnect)
                {
                    await _mockPlcService.ConnectAsync(cancellationToken);
                }
                var data = await _mockPlcService.ReadSensorDataAsync(cancellationToken);
                if (data == null)
                {
                    if (!_userRequestedDisconnect && CurrentState == PlcConnectionState.Connected)
                    {
                        HandleUnexpectedDisconnect();
                    }
                    return null;
                }
                return data;
            }
            else
            {
                var data = await _realPlcService.ReadSensorDataAsync(cancellationToken);
                if (data == null)
                {
                    // Gerçek PLC bağlantısı koptu (kablo çekildi / soket kapandı)
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
                _logger.LogWarning("PLC bağlantısı beklenmedik şekilde koptu! Durum: Reconnecting. Exponential Backoff ile otomatik toparlanma başlatılıyor...");
                OnConnectionEvent?.Invoke(PlcConnectionEvent.PlcReconnecting, "PLC bağlantısı beklenmedik şekilde koptu! Otomatik toparlanma devrede.");

                StartAutoReconnect();
            }
        }

        // Sprint 1.2: Exponential Backoff Stratejisi (2s -> 4s -> 8s -> 16s -> 30s Max)
        private void StartAutoReconnect()
        {
            StopAutoReconnect();

            _reconnectCts = new CancellationTokenSource();
            var token = _reconnectCts.Token;

            _ = Task.Run(async () =>
            {
                int currentDelaySec = 2; // Başlangıç: 2 saniye
                int attemptCount = 0;

                while (!token.IsCancellationRequested && !_userRequestedDisconnect && CurrentState == PlcConnectionState.Reconnecting)
                {
                    try
                    {
                        attemptCount++;
                        _logger.LogInformation("[Auto-Reconnect] Deneme #{Attempt}. {Delay} saniye bekleniyor...", attemptCount, currentDelaySec);
                        
                        await Task.Delay(TimeSpan.FromSeconds(currentDelaySec), token);
                        if (token.IsCancellationRequested || _userRequestedDisconnect) break;

                        if (IsSimulationMode)
                        {
                            if (CurrentScenario != SimulationScenario.PlcDisconnect)
                            {
                                await _mockPlcService.ConnectAsync(token);
                                CurrentState = PlcConnectionState.Connected;
                                _logger.LogInformation("Simülasyon bağlantısı otomatik olarak yeniden kuruldu!");
                                OnConnectionEvent?.Invoke(PlcConnectionEvent.PlcReconnected, "Simülasyon bağlantısı otomatik olarak yeniden kuruldu!");
                                break;
                            }
                        }
                        else
                        {
                            var connected = await _realPlcService.ConnectAsync(token);
                            if (connected)
                            {
                                CurrentState = PlcConnectionState.Connected;
                                _logger.LogInformation("Gerçek PLC bağlantısı otomatik olarak yeniden kuruldu (ONLINE)!");
                                OnConnectionEvent?.Invoke(PlcConnectionEvent.PlcReconnected, "Gerçek PLC bağlantısı otomatik olarak yeniden kuruldu!");
                                break;
                            }
                        }

                        // Üstel artan bekleme (Max: 30 saniye)
                        currentDelaySec = Math.Min(30, currentDelaySec * 2);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning("Yeniden bağlanma denemesi başarısız oldu: {Message}. Üstel bekleme uygulanıyor...", ex.Message);
                        currentDelaySec = Math.Min(30, currentDelaySec * 2);
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